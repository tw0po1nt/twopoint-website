namespace TwoPoint.Dependencies.Posts

open TwoPoint.Core.Posts
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Util

open Azure
open Azure.Storage.Blobs
open Azure.Storage.Blobs.Models
open FSharp.Control
open FsToolkit.ErrorHandling
open IcedTasks
open Microsoft.Extensions.Logging

open System

/// <summary>
/// The post store keeps each version of a post as the blob <c>&lt;slug&gt;.md</c> in one of four containers.
/// </summary>
type PostStore(service : BlobServiceClient, logger : ILogger) =

  let extension = ".md"

  let read (container : string) : DependencyResult<StoredPost list> =
    runDependencyWithLogging Dependency.BlobStorage logger $"{nameof PostStore}.Read({container})"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let client = service.GetBlobContainerClient container

      try
        let! blobs = client.GetBlobsAsync(cancellationToken = ct) |> TaskSeq.toListAsync

        let posts = ResizeArray()

        for blob in blobs |> List.filter (fun blob -> blob.Name.EndsWith extension && not (blob.Name.Contains '/')) do
          match Slug.create (blob.Name.Substring(0, blob.Name.Length - extension.Length)) with
          | Ok slug ->
            let! content = client.GetBlobClient(blob.Name).DownloadContentAsync(ct)
            posts.Add({ Slug = slug; Text = content.Value.Content.ToString() } : StoredPost)
          | Error reason ->
            logger.LogWarning("The blob '{blobName}' in '{container}' is not a post: {reason}", blob.Name, container, reason)

        return List.ofSeq posts
      with :? RequestFailedException as error when error.ErrorCode = "ContainerNotFound" ->
        return []
    }

  let blobName (slug : Slug) = $"{Slug.value slug}{extension}"

  let notFound (error : RequestFailedException) =
    error.Status = 404 && (error.ErrorCode = "BlobNotFound" || error.ErrorCode = "ContainerNotFound")

  let readBlob (container : string) (slug : Slug) : DependencyResult<StoredBlob option> =
    runDependencyWithLogging Dependency.BlobStorage logger $"{nameof PostStore}.ReadBlob({container}, {slug})"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()

      try
        let! download = service.GetBlobContainerClient(container).GetBlobClient(blobName slug).DownloadContentAsync(ct)

        return Some { Text = download.Value.Content.ToString(); ETag = download.Value.Details.ETag.ToString() }
      with :? RequestFailedException as error when notFound error ->
        return None
    }

  let listSlugs (container : string) : DependencyResult<Slug list> =
    runDependencyWithLogging Dependency.BlobStorage logger $"{nameof PostStore}.ListSlugs({container})"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()

      try
        let! blobs = service.GetBlobContainerClient(container).GetBlobsAsync(cancellationToken = ct) |> TaskSeq.toListAsync

        return
          blobs
          |> List.filter (fun blob -> blob.Name.EndsWith extension && not (blob.Name.Contains '/'))
          |> List.choose (fun blob ->
            Slug.create (blob.Name.Substring(0, blob.Name.Length - extension.Length)) |> Result.toOption)
      with :? RequestFailedException as error when error.ErrorCode = "ContainerNotFound" ->
        return []
    }

  /// A series record is the blob `<slug>.yml` in the `series` container.
  member _.GetSeriesRecords () : DependencyResult<StoredSeries list> =
    runDependencyWithLogging Dependency.BlobStorage logger $"{nameof PostStore}.GetSeriesRecords"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let client = service.GetBlobContainerClient "series"

      try
        let! blobs = client.GetBlobsAsync(cancellationToken = ct) |> TaskSeq.toListAsync
        let records = ResizeArray()

        for blob in blobs |> List.filter (fun blob -> blob.Name.EndsWith ".yml" && not (blob.Name.Contains '/')) do
          let! content = client.GetBlobClient(blob.Name).DownloadContentAsync(ct)
          records.Add { Slug = blob.Name.Substring(0, blob.Name.Length - ".yml".Length); Text = content.Value.Content.ToString() }

        return List.ofSeq records
      with :? RequestFailedException as error when error.ErrorCode = "ContainerNotFound" ->
        return []
    }

  /// A blob that is gone or has another ETag gives <c>DeleteConditionFailed</c>.
  member _.DeleteWorkingBlob (slug : Slug) (eTag : string) : DependencyResult<DeleteOutcome> =
    runDependencyWithLogging Dependency.BlobStorage logger $"{nameof PostStore}.DeleteWorkingBlob({slug})"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let conditions = BlobRequestConditions(IfMatch = ETag eTag)

      try
        let! _ =
          service.GetBlobContainerClient("working").GetBlobClient(blobName slug).DeleteAsync(DeleteSnapshotsOption.None, conditions, ct)

        return Deleted
      with :? RequestFailedException as error when error.Status = 412 || notFound error ->
        return DeleteConditionFailed
    }

  member _.GetPublishedPosts () = read "published"

  member _.GetWorkingPosts () = read "working"

  member _.GetPublishedBlob (slug : Slug) = readBlob "published" slug

  member _.GetWorkingBlob (slug : Slug) = readBlob "working" slug

  member _.GetPostSlugs () : DependencyResult<Slug list> =
    cancellableTaskResult {
      let! published, working = CancellableTaskResult.parallelZip (listSlugs "published") (listSlugs "working")
      return List.append published working |> List.distinct
    }

  /// A failed condition gives <c>ConditionFailed</c> and leaves the blob as it was.
  member _.WriteWorkingBlob (slug : Slug) (text : string) (condition : WriteCondition) : DependencyResult<WriteOutcome> =
    runDependencyWithLogging Dependency.BlobStorage logger $"{nameof PostStore}.WriteWorkingBlob({slug})"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let container = service.GetBlobContainerClient "working"
      let conditions = BlobRequestConditions()

      match condition with
      | MatchesETag eTag -> conditions.IfMatch <- ETag eTag
      | MustNotExist -> conditions.IfNoneMatch <- ETag.All

      let options = BlobUploadOptions(Conditions = conditions)

      let write () =
        cancellableTaskResult {
          let! response = container.GetBlobClient(blobName slug).UploadAsync(BinaryData.FromString text, options, ct)
          return Written(response.Value.ETag.ToString())
        }

      try
        try
          return! write ()
        with :? RequestFailedException as error when error.ErrorCode = "ContainerNotFound" ->
          // Azurite starts with no container.
          do! container.CreateIfNotExistsAsync(cancellationToken = ct) |> Task.ignore
          return! write ()
      with :? RequestFailedException as error when error.Status = 412 || error.ErrorCode = "BlobAlreadyExists" ->
        return ConditionFailed
    }
