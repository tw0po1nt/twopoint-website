namespace TwoPoint.Site.Generator

open Azure.Identity
open Azure.Storage.Blobs
open Azure.Storage.Blobs.Models
open FsToolkit.ErrorHandling
open System

type Blob =
  { Name: string
    ContentType: string
    CacheControl: string
    Content: byte array }

type IBlobContainer =
  abstract CreateIfNotExists: unit -> Result<unit, string>
  /// Replaces a blob that has the same name.
  abstract Upload: Blob -> Result<unit, string>

[<RequireQualifiedAccess>]
module BlobContainer =

  let azure (container: Uri) =
    let client = BlobContainerClient(container, DefaultAzureCredential())

    { new IBlobContainer with
        member _.CreateIfNotExists() =
          AzureRequest.attempt (fun () -> client.CreateIfNotExists() |> ignore)

        member _.Upload blob =
          let options =
            BlobUploadOptions(
              HttpHeaders = BlobHttpHeaders(ContentType = blob.ContentType, CacheControl = blob.CacheControl)
            )

          AzureRequest.attempt (fun () -> client.GetBlobClient(blob.Name).Upload(BinaryData.FromBytes blob.Content, options) |> ignore) }

[<RequireQualifiedAccess>]
module Upload =

  /// The backend reads each newsletter at this name.
  let blobName (slug: string) = $"posts/{slug}/index"

  let private blob (newsletter: Disk.NewsletterFile) =
    { Name = blobName newsletter.Slug
      ContentType = "text/html"
      CacheControl = "public, max-age=3600"
      Content = newsletter.Html }

  let private checkStage (stageMarker: string option) =
    let production = Stage.name Stage.Production

    match stageMarker |> Option.map (fun text -> text, Stage.parse text) with
    | None -> Error $"Found no stage marker. Run build --stage {production} before upload."
    | Some(_, Some Stage.Production) -> Ok()
    | Some(_, Some stage) ->
      Error $"The build stage is {Stage.name stage}. upload sends only a {production} build."
    | Some(text, None) -> Error $"The stage marker contains '{text}', which is not a stage."

  let run
    (container: IBlobContainer)
    (stageMarker: string option)
    (newsletters: Disk.NewsletterFile list)
    : Result<string list, string> =
    result {
      do! checkStage stageMarker

      do!
        if List.isEmpty newsletters then
          Error "Found no newsletters. Run build before upload."
        else
          Ok()

      do! container.CreateIfNotExists() |> Result.mapError (fun reason -> $"Cannot make the container: {reason}")

      let blobs = newsletters |> List.map blob

      do!
        blobs
        |> List.traverseResultM (fun blob ->
          container.Upload blob
          |> Result.mapError (fun reason -> $"Cannot upload {blob.Name}: {reason}"))
        |> Result.ignore

      return blobs |> List.map _.Name
    }
