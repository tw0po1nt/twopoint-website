// Uploads the posts and the series records from Git into the post store.
// Run it from the repo root after `az login`:
//   dotnet fsi scripts/migrate-posts/migrate-posts.fsx --account <name> [--dry-run]
// Run it against Azurite on the default ports:
//   dotnet fsi scripts/migrate-posts/migrate-posts.fsx --azurite

#r "nuget: Azure.Identity, 1.20.0"
#r "nuget: Azure.Storage.Blobs, 12.30.1"

open Azure
open Azure.Identity
open Azure.Storage.Blobs
open Azure.Storage.Blobs.Models
open System
open System.Globalization
open System.IO
open System.Text.RegularExpressions

let postsFolder = Path.Combine("frontend", "content", "posts", "published")
let seriesFolder = Path.Combine("frontend", "content", "posts", "series")

/// The dashboard does not count a post with this mark as a delivery to start.
let migratedMark = dict [ "migrated", "true" ]

let publishDateLine = Regex(@"^publishDate:\s*['""]?([^\s'""]+)", RegexOptions.Multiline)

let fail (message: string) =
  eprintfn "%s" message
  exit 1

type Upload =
  { Container: string
    Name: string
    File: string
    ContentType: string
    Metadata: Collections.Generic.IDictionary<string, string> }

let publishDate (file: string) =
  let frontMatter = (File.ReadAllText file).Split("\n---", 2)[0]
  let found = publishDateLine.Match frontMatter

  if not found.Success then
    fail $"{file} has no publishDate."

  let parsed, date =
    DateTime.TryParse(
      found.Groups[1].Value,
      CultureInfo.InvariantCulture,
      DateTimeStyles.AdjustToUniversal ||| DateTimeStyles.AssumeUniversal
    )

  if not parsed then
    fail $"The publishDate of {file} is not a date."

  date.ToString("O", CultureInfo.InvariantCulture)

let files folder pattern =
  Directory.GetFiles(folder, pattern) |> Array.sort |> List.ofArray

let slug (file: string) = Path.GetFileNameWithoutExtension file

let uploads () =
  let posts = files postsFolder "*.md"
  let series = files seriesFolder "*.yml"

  if List.isEmpty posts then
    fail $"{postsFolder} contains no post."

  let markdown = "text/markdown; charset=utf-8"
  let noMetadata = dict []

  [ for file in posts do
      { Container = "published"
        Name = $"{slug file}.md"
        File = file
        ContentType = markdown
        Metadata = migratedMark }

      { Container = "revisions"
        Name = $"{slug file}/{publishDate file}.md"
        File = file
        ContentType = markdown
        Metadata = noMetadata }
    for file in series do
      { Container = "series"
        Name = $"{slug file}.yml"
        File = file
        ContentType = "application/yaml; charset=utf-8"
        Metadata = noMetadata } ]

/// `If-None-Match: *` makes the write fail on a blob that exists, so a second run changes no blob.
let upload (service: BlobServiceClient) (item: Upload) =
  let options =
    BlobUploadOptions(
      HttpHeaders = BlobHttpHeaders(ContentType = item.ContentType),
      Metadata = item.Metadata,
      Conditions = BlobRequestConditions(IfNoneMatch = ETag.All)
    )

  let blob = service.GetBlobContainerClient(item.Container).GetBlobClient item.Name

  try
    blob.Upload(BinaryData.FromBytes(File.ReadAllBytes item.File), options) |> ignore
    printfn "  wrote the blob"
    1
  with :? RequestFailedException as error when error.ErrorCode = "BlobAlreadyExists" ->
    printfn "  the blob is already in the store"
    0

let containers = [ "published"; "working"; "revisions"; "series" ]

/// The wizard makes the containers in Azure. Azurite starts empty, so the script makes them there.
let makeContainers (service: BlobServiceClient) =
  for name in containers do
    service.GetBlobContainerClient(name).CreateIfNotExists() |> ignore

/// With `dryRun`, the script writes no blob.
let migrate (service: BlobServiceClient) dryRun =
  let written =
    uploads ()
    |> List.sumBy (fun item ->
      printfn "%s -> %s/%s" item.File item.Container item.Name

      if dryRun then 0 else upload service item)

  if not dryRun then
    printfn "Wrote %d blobs." written

let account (name: string) =
  BlobServiceClient(Uri $"https://{name}.blob.core.windows.net", AzureCliCredential())

let azurite () =
  BlobServiceClient "UseDevelopmentStorage=true"

match fsi.CommandLineArgs |> Array.tail |> List.ofArray with
| [ "--account"; name ] -> migrate (account name) false
| [ "--account"; name; "--dry-run" ] -> migrate (account name) true
| [ "--azurite" ] ->
  let service = azurite ()
  makeContainers service
  migrate service false
| _ ->
  fail "Usage: dotnet fsi scripts/migrate-posts/migrate-posts.fsx [--account <name> [--dry-run] | --azurite]"
