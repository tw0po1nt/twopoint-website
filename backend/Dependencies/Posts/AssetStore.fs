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

/// The public `assets` container keeps each image and each PDF under the name of its hash.
type AssetStore(client : AssetsClient, logger : ILogger) =

  let cacheControl = "public, max-age=31536000, immutable"

  /// A blob never changes, so a blob with the same name has the same bytes and a second write is not necessary.
  member _.WriteAsset (name : string) (contentType : string) (content : byte[]) : DependencyResult<Uri> =
    runDependencyWithLogging Dependency.BlobStorage logger $"{nameof AssetStore}.WriteAsset({name})"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let container = client.Service.GetBlobContainerClient "assets"
      let blob = container.GetBlobClient name

      let options =
        BlobUploadOptions(
          HttpHeaders = BlobHttpHeaders(ContentType = contentType, CacheControl = cacheControl),
          Conditions = BlobRequestConditions(IfNoneMatch = ETag.All)
        )

      let write () =
        cancellableTask {
          let! _ = blob.UploadAsync(BinaryData content, options, ct)
          ()
        }

      try
        try
          do! write ()
        with :? RequestFailedException as error when error.ErrorCode = "ContainerNotFound" ->
          // Azurite starts with no container.
          do! container.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken = ct) |> Task.ignore
          do! write ()
      with :? RequestFailedException as error when error.Status = 409 || error.Status = 412 ->
        logger.LogInformation("The asset '{name}' exists", name)

      return blob.Uri
    }
