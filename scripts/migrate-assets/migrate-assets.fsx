// Copies each old asset of the posts into the `assets` container, and changes the URLs in the posts.
// Run it from the repo root after `az login`:
//   dotnet fsi scripts/migrate-assets/migrate-assets.fsx [--dry-run]
// Apply the URL map to a folder, for example a build from `main`:
//   dotnet fsi scripts/migrate-assets/migrate-assets.fsx apply-map <folder>

#r "nuget: Azure.Identity, 1.20.0"
#r "nuget: Azure.Storage.Blobs, 12.30.1"

open Azure
open Azure.Identity
open Azure.Storage.Blobs
open Azure.Storage.Blobs.Models
open System
open System.IO
open System.Net.Http
open System.Security.Cryptography
open System.Text.Encodings.Web
open System.Text.Json
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks

let postsFolder = Path.Combine("frontend", "content", "posts", "published")
let mapFile = Path.Combine(__SOURCE_DIRECTORY__, "url-map.json")
let container = Uri "https://twopointwebsite.blob.core.windows.net/assets"
let cacheControl = "public, max-age=31536000, immutable"

let oldBlobUrl =
  Regex @"https://twopointwebsite\.blob\.core\.windows\.net/(?:posts|series|thumbnails)/[^\s)""'\]]+"

let frontMatterImage = Regex(@"^\s*image:\s*['""]?(https?://[^\s'""]+)", RegexOptions.Multiline)

let fail (message: string) =
  eprintfn "%s" message
  exit 1

let posts () =
  Directory.GetFiles(postsFolder, "*.md") |> Array.sort

/// The blob URLs in the old containers, and each `og:image` source in the front matter.
let oldUrls () =
  posts ()
  |> Seq.collect (fun file ->
    let text = File.ReadAllText file
    let frontMatter = text.Split("\n---", 2)[0]

    Seq.append
      (oldBlobUrl.Matches text |> Seq.map _.Value)
      (frontMatterImage.Matches frontMatter |> Seq.map _.Groups[1].Value))
  |> Seq.filter (fun url -> not (url.StartsWith(container.AbsoluteUri + "/")))
  |> Seq.distinct
  |> Seq.sort
  |> List.ofSeq

// The build image fetch sends no Accept header, and the Unsplash bytes change with that header.
let client = new HttpClient(Timeout = TimeSpan.FromSeconds 30.0)

let rec download (url: string) attempt =
  try
    use response = client.GetAsync(Uri url).GetAwaiter().GetResult()

    if response.IsSuccessStatusCode then
      response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult()
    elif attempt < 3 then
      Thread.Sleep(TimeSpan.FromSeconds 1.0)
      download url (attempt + 1)
    else
      fail $"HTTP {int response.StatusCode} for {url}"
  with
  | :? TaskCanceledException
  | :? HttpRequestException when attempt < 3 ->
    Thread.Sleep(TimeSpan.FromSeconds 1.0)
    download url (attempt + 1)

let startsWith (prefix: byte array) (offset: int) (bytes: byte array) =
  bytes.Length >= offset + prefix.Length
  && bytes.AsSpan(offset, prefix.Length).SequenceEqual(ReadOnlySpan prefix)

let ascii (text: string) = Text.Encoding.ASCII.GetBytes text

/// The extension and the `Content-Type` from the first bytes of the file.
let fileType (url: string) (bytes: byte array) =
  if startsWith [| 0xFFuy; 0xD8uy; 0xFFuy |] 0 bytes then "jpg", "image/jpeg"
  elif startsWith [| 0x89uy; 0x50uy; 0x4Euy; 0x47uy |] 0 bytes then "png", "image/png"
  elif startsWith (ascii "GIF8") 0 bytes then "gif", "image/gif"
  elif startsWith (ascii "RIFF") 0 bytes && startsWith (ascii "WEBP") 8 bytes then "webp", "image/webp"
  elif startsWith (ascii "%PDF") 0 bytes then "pdf", "application/pdf"
  else fail $"The file at {url} is not a JPEG, PNG, GIF, WebP, or PDF file."

let blobName (bytes: byte array) extension =
  let hash = Convert.ToHexStringLower(SHA256.HashData bytes)
  $"{hash.Substring(0, 16)}.{extension}"

let upload (blobs: BlobContainerClient) name contentType (bytes: byte array) =
  let options =
    BlobUploadOptions(
      HttpHeaders = BlobHttpHeaders(ContentType = contentType, CacheControl = cacheControl),
      Conditions = BlobRequestConditions(IfNoneMatch = ETag.All)
    )

  try
    blobs.GetBlobClient(name).Upload(BinaryData.FromBytes bytes, options) |> ignore
    printfn "  wrote the blob"
  with :? RequestFailedException as error when error.Status = 409 ->
    printfn "  the blob is already in the container"

/// The longest URL goes first, so that no URL changes a part of a longer URL.
let replaceAll (map: (string * string) list) (text: string) =
  map
  |> List.sortByDescending (fst >> String.length)
  |> List.fold (fun (text: string) (oldUrl, newUrl) -> text.Replace(oldUrl, newUrl)) text

let writeMap (map: (string * string) list) =
  let json =
    JsonSerializer.Serialize(
      dict map,
      JsonSerializerOptions(WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)
    )

  File.WriteAllText(mapFile, json + "\n")

let readMap () =
  JsonSerializer.Deserialize<Collections.Generic.Dictionary<string, string>>(File.ReadAllText mapFile)
  |> Seq.map (fun pair -> pair.Key, pair.Value)
  |> List.ofSeq

let rewrite (map: (string * string) list) (file: string) =
  let text = File.ReadAllText file
  let changed = replaceAll map text

  if changed <> text then
    File.WriteAllText(file, changed)
    printfn "  changed %s" file

/// With `dryRun`, the script writes no blob, no map, and no post.
let migrate dryRun =
  let blobs = BlobContainerClient(container, AzureCliCredential())

  let urls = oldUrls ()

  if List.isEmpty urls then
    fail "The posts contain no old URL. The map in url-map.json stays as it is."

  let map =
    urls
    |> List.map (fun url ->
      printfn "%s" url
      let bytes = download url 1
      let extension, contentType = fileType url bytes
      let name = blobName bytes extension
      printfn "  %s, %s, %d bytes" name contentType bytes.Length

      if not dryRun then
        upload blobs name contentType bytes

      url, $"{container.AbsoluteUri}/{name}")

  if not dryRun then
    writeMap map
    printfn "Wrote %s" mapFile
    posts () |> Array.iter (rewrite map)

let applyMap (folder: string) =
  let map = readMap ()

  Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
  |> Array.filter (fun file -> [ ".html"; ".xml"; ".txt" ] |> List.contains (Path.GetExtension file))
  |> Array.iter (rewrite map)

match fsi.CommandLineArgs |> Array.tail |> List.ofArray with
| [] -> migrate false
| [ "--dry-run" ] -> migrate true
| [ "apply-map"; folder ] -> applyMap folder
| _ -> fail "Usage: dotnet fsi scripts/migrate-assets/migrate-assets.fsx [--dry-run | apply-map <folder>]"
