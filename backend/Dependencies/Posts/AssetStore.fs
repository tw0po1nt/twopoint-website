namespace TwoPoint.Dependencies.Posts

open TwoPoint.Core.Util
open TwoPoint.Dependencies.Util

open Azure
open Azure.Storage.Blobs
open Azure.Storage.Blobs.Models
open FsToolkit.ErrorHandling
open IcedTasks
open Microsoft.Extensions.Logging

open System

/// The account of the `assets` container. It is not the account of the post store.
type AssetsClient = { Service : BlobServiceClient }

type AssetStore(client : AssetsClient, logger : ILogger) =

  let cacheControl = "public, max-age=31536000, immutable"

  member _.WriteAsset (name : string) (contentType : string) (content : byte[]) : DependencyResult<Uri> =
    runDependencyWithLogging Dependency.BlobStorage logger $"{nameof AssetStore}.WriteAsset({name})"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let blob = client.Service.GetBlobContainerClient("assets").GetBlobClient name

      let options =
        BlobUploadOptions(
          HttpHeaders = BlobHttpHeaders(ContentType = contentType, CacheControl = cacheControl),
          Conditions = BlobRequestConditions(IfNoneMatch = ETag.All)
        )

      try
        let! _ = blob.UploadAsync(BinaryData content, options, ct)
        ()
      with :? RequestFailedException as error when error.Status = 409 || error.Status = 412 ->
        logger.LogInformation("The asset '{name}' exists", name)

      return blob.Uri
    }
