namespace TwoPoint.Dependencies.Posts

open TwoPoint.Core.Posts
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Util

open Azure
open Azure.Storage.Blobs
open FSharp.Control
open FsToolkit.ErrorHandling
open IcedTasks
open Microsoft.Extensions.Logging

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

        let texts = ResizeArray()

        for blob in blobs |> List.filter (fun blob -> blob.Name.EndsWith extension && not (blob.Name.Contains '/')) do
          let! content = client.GetBlobClient(blob.Name).DownloadContentAsync(ct)
          texts.Add(blob.Name.Substring(0, blob.Name.Length - extension.Length), content.Value.Content.ToString())

        return!
          List.ofSeq texts
          |> List.traverseResultM (fun (slug, text) ->
            Slug.create slug
            |> Result.map (fun slug -> { Slug = slug; Text = text })
            |> Result.mapError (fun reason ->
              { Dependency = Dependency.BlobStorage
                Type = DependencyErrorType.Validation $"The blob '{slug}{extension}' in '{container}' is not a post: {reason}" }))
      with :? RequestFailedException as error when error.ErrorCode = "ContainerNotFound" ->
        return []
    }

  member _.GetPublishedPosts () = read "published"

  member _.GetWorkingPosts () = read "working"
