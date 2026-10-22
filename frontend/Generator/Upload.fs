namespace TwoPoint.Site.Generator

open TwoPoint.Site.Render

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
  abstract Names: prefix: string -> Result<string list, string>
  /// Gives no error when the blob does not exist.
  abstract Delete: name: string -> Result<unit, string>

type UploadReport =
  { Uploaded: string list
    Deleted: string list }

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

          AzureRequest.attempt (fun () -> client.GetBlobClient(blob.Name).Upload(BinaryData.FromBytes blob.Content, options) |> ignore)

        member _.Names prefix =
          AzureRequest.attempt (fun () -> [ for item in client.GetBlobs(GetBlobsOptions(Prefix = prefix)) -> item.Name ])

        member _.Delete name =
          AzureRequest.attempt (fun () ->
            client.GetBlobClient(name).DeleteIfExists(DeleteSnapshotsOption.IncludeSnapshots) |> ignore) }

[<RequireQualifiedAccess>]
module Upload =

  /// The backend reads each newsletter at this name.
  let blobName (slug: string) = $"posts/{slug}/index"

  let private prefix = "posts/"

  let private isNewsletter (name: string) =
    match name.Split '/' with
    | [| "posts"; slug; "index" |] -> slug <> ""
    | _ -> false

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
    : Result<UploadReport, string> =
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

      let uploaded = blobs |> List.map _.Name

      let! names = container.Names prefix |> Result.mapError (fun reason -> $"Cannot list the newsletters: {reason}")

      let removed =
        names
        |> List.filter (fun name -> isNewsletter name && not (List.contains name uploaded))

      do!
        removed
        |> List.traverseResultM (fun name ->
          container.Delete name
          |> Result.mapError (fun reason -> $"Cannot delete {name}: {reason}"))
        |> Result.ignore

      return { Uploaded = uploaded; Deleted = removed }
    }
