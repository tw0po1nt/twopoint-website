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
open System.Threading
open System.Threading.Tasks

/// <summary>
/// The post store keeps each version of a post as the blob <c>&lt;slug&gt;.md</c> in one of four containers.
/// </summary>
type PostStore(service : BlobServiceClient, logger : ILogger, texts : BlobTextCache) =

  let extension = ".md"

  let downloadText (container : string) (client : BlobContainerClient) (ct : CancellationToken) (blob : BlobItem) : Task<string> =
    task {
      let listed = blob.Properties.ETag |> Option.ofNullable |> Option.map string

      match listed |> Option.bind (texts.TryFind container blob.Name) with
      | Some text -> return text
      | None ->
        let! content = client.GetBlobClient(blob.Name).DownloadContentAsync(ct)
        let text = content.Value.Content.ToString()
        texts.Add container blob.Name (string content.Value.Details.ETag) text
        return text
    }

  let listAndDownload (container : string) (isBlob : BlobItem -> bool) (ct : CancellationToken) =
    task {
      let client = service.GetBlobContainerClient container
      let! blobs = client.GetBlobsAsync(cancellationToken = ct) |> TaskSeq.toListAsync
      let wanted = blobs |> List.filter isBlob

      wanted
      |> List.choose (fun blob -> blob.Properties.ETag |> Option.ofNullable |> Option.map (fun eTag -> blob.Name, string eTag))
      |> Set.ofList
      |> texts.Retain container

      let! downloaded = wanted |> List.map (downloadText container client ct) |> Task.WhenAll
      return List.zip wanted (List.ofArray downloaded)
    }

  let read (container : string) : DependencyResult<StoredPost list> =
    runDependencyWithLogging Dependency.BlobStorage logger $"{nameof PostStore}.Read({container})"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()

      try
        let! blobs =
          listAndDownload container (fun blob -> blob.Name.EndsWith extension && not (blob.Name.Contains '/')) ct

        return
          blobs
          |> List.choose (fun (blob, text) ->
            match Slug.create (blob.Name.Substring(0, blob.Name.Length - extension.Length)) with
            | Ok slug -> Some({ Slug = slug; Text = text; ETag = string blob.Properties.ETag } : StoredPost)
            | Error reason ->
              logger.LogWarning("The blob '{blobName}' in '{container}' is not a post: {reason}", blob.Name, container, reason)
              None)
      with :? RequestFailedException as error when error.ErrorCode = "ContainerNotFound" ->
        return []
    }

  let blobName (slug : Slug) = $"{Slug.value slug}{extension}"

  let migratedKey = "migrated"

  let markOf (metadata : Collections.Generic.IDictionary<string, string>) =
    match metadata.TryGetValue migratedKey with
    | true, "true" -> Migrated
    | _ -> NotMigrated

  let metadataOf (mark : MigratedMark) =
    match mark with
    | Migrated -> dict [ migratedKey, "true" ]
    | NotMigrated -> dict []

  let notFound (error : RequestFailedException) =
    error.Status = 404 && (error.ErrorCode = "BlobNotFound" || error.ErrorCode = "ContainerNotFound")

  let readBlob (container : string) (slug : Slug) : DependencyResult<StoredBlob option> =
    runDependencyWithLogging Dependency.BlobStorage logger $"{nameof PostStore}.ReadBlob({container}, {slug})"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()

      try
        let! download = service.GetBlobContainerClient(container).GetBlobClient(blobName slug).DownloadContentAsync(ct)

        return
          Some
            { Text = download.Value.Content.ToString()
              ETag = download.Value.Details.ETag.ToString()
              Mark = markOf download.Value.Details.Metadata }
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

  let revisionName (slug : Slug) (time : DateTime) =
    $"{Slug.value slug}/{TwoPoint.Site.Render.Time.isoTime (DateTimeOffset time)}{extension}"

  let conditionsOf (condition : WriteCondition) =
    let conditions = BlobRequestConditions()

    match condition with
    | MatchesETag eTag -> conditions.IfMatch <- ETag eTag
    | MustNotExist -> conditions.IfNoneMatch <- ETag.All

    conditions

  let writeBlob
    (container : string)
    (name : string)
    (text : string)
    (mark : MigratedMark)
    (conditions : BlobRequestConditions)
    : DependencyResult<WriteOutcome> =
    cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let containerClient = service.GetBlobContainerClient container
      // An upload replaces the metadata of the blob.
      let options = BlobUploadOptions(Conditions = conditions, Metadata = metadataOf mark)

      let write () =
        cancellableTaskResult {
          let! response = containerClient.GetBlobClient(name).UploadAsync(BinaryData.FromString text, options, ct)
          return Written(response.Value.ETag.ToString())
        }

      try
        try
          return! write ()
        with :? RequestFailedException as error when error.ErrorCode = "ContainerNotFound" ->
          // Azurite starts with no container.
          do! containerClient.CreateIfNotExistsAsync(cancellationToken = ct) |> Task.ignore
          return! write ()
      with :? RequestFailedException as error when error.Status = 412 || error.ErrorCode = "BlobAlreadyExists" ->
        return ConditionFailed
    }

  /// A series record is the blob `<slug>.yml` in the `series` container.
  member _.GetSeriesRecords () : DependencyResult<StoredSeries list> =
    runDependencyWithLogging Dependency.BlobStorage logger $"{nameof PostStore}.GetSeriesRecords"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()

      try
        let! blobs = listAndDownload "series" (fun blob -> blob.Name.EndsWith ".yml" && not (blob.Name.Contains '/')) ct

        return
          blobs
          |> List.map (fun (blob, text) ->
            { Slug = blob.Name.Substring(0, blob.Name.Length - ".yml".Length)
              Text = text
              ETag = string blob.Properties.ETag })
      with :? RequestFailedException as error when error.ErrorCode = "ContainerNotFound" ->
        return []
    }

  /// A failed condition gives <c>ConditionFailed</c> and leaves the record as it was.
  member _.WriteSeriesBlob (slug : Slug) (text : string) (condition : WriteCondition) : DependencyResult<WriteOutcome> =
    runDependencyWithLogging Dependency.BlobStorage logger $"{nameof PostStore}.WriteSeriesBlob({slug})"
    <| writeBlob "series" $"{Slug.value slug}.yml" text NotMigrated (conditionsOf condition)

  /// A blob that is gone or has another ETag gives <c>DeleteConditionFailed</c>.
  member _.DeleteSeriesBlob (slug : Slug) (eTag : string) : DependencyResult<DeleteOutcome> =
    runDependencyWithLogging Dependency.BlobStorage logger $"{nameof PostStore}.DeleteSeriesBlob({slug})"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let conditions = BlobRequestConditions(IfMatch = ETag eTag)

      try
        let! _ =
          service.GetBlobContainerClient("series").GetBlobClient($"{Slug.value slug}.yml").DeleteAsync(DeleteSnapshotsOption.None, conditions, ct)

        return Deleted
      with :? RequestFailedException as error when error.Status = 412 || notFound error ->
        return DeleteConditionFailed
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

  /// A blob that is gone or has another ETag gives <c>DeleteConditionFailed</c>.
  member _.DeletePublishedBlob (slug : Slug) (eTag : string) : DependencyResult<DeleteOutcome> =
    runDependencyWithLogging Dependency.BlobStorage logger $"{nameof PostStore}.DeletePublishedBlob({slug})"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let conditions = BlobRequestConditions(IfMatch = ETag eTag)

      try
        let! _ =
          service.GetBlobContainerClient("published").GetBlobClient(blobName slug).DeleteAsync(DeleteSnapshotsOption.None, conditions, ct)

        return Deleted
      with :? RequestFailedException as error when error.Status = 412 || notFound error ->
        return DeleteConditionFailed
    }

  member _.DeleteRevisionBlobs (slug : Slug) : DependencyResult<unit> =
    runDependencyWithLogging Dependency.BlobStorage logger $"{nameof PostStore}.DeleteRevisionBlobs({slug})"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let revisions = service.GetBlobContainerClient "revisions"

      try
        let! blobs = revisions.GetBlobsAsync(BlobTraits.None, BlobStates.None, $"{Slug.value slug}/", ct) |> TaskSeq.toListAsync

        for blob in blobs do
          do! revisions.GetBlobClient(blob.Name).DeleteIfExistsAsync(cancellationToken = ct) |> Task.ignore
      with :? RequestFailedException as error when error.ErrorCode = "ContainerNotFound" ->
        ()
    }

  /// A blob whose name is not a revision name is not in the list.
  member _.GetRevisionBlobs (slug : Slug) : DependencyResult<StoredRevision list> =
    runDependencyWithLogging Dependency.BlobStorage logger $"{nameof PostStore}.GetRevisionBlobs({slug})"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let revisions = service.GetBlobContainerClient "revisions"
      let prefix = $"{Slug.value slug}/"

      try
        let! blobs = revisions.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, ct) |> TaskSeq.toListAsync

        let named =
          blobs
          |> List.filter (fun blob -> blob.Name.EndsWith extension)
          |> List.choose (fun blob ->
            match RevisionName.create (blob.Name.Substring(prefix.Length, blob.Name.Length - prefix.Length - extension.Length)) with
            | Ok name -> Some(blob, name)
            | Error reason ->
              logger.LogWarning("The blob '{blobName}' in 'revisions' is not a revision: {reason}", blob.Name, reason)
              None)

        let! texts =
          named
          |> List.map (fun (blob, _) ->
            task {
              let! content = revisions.GetBlobClient(blob.Name).DownloadContentAsync(ct)
              return content.Value.Content.ToString()
            })
          |> Task.WhenAll

        return List.map2 (fun (_, name) text -> { Name = name; Text = text }) named (List.ofArray texts)
      with :? RequestFailedException as error when error.ErrorCode = "ContainerNotFound" ->
        return []
    }

  member _.GetRevisionBlob (slug : Slug) (name : RevisionName) : DependencyResult<string option> =
    runDependencyWithLogging Dependency.BlobStorage logger $"{nameof PostStore}.GetRevisionBlob({slug}, {name})"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let blob = $"{Slug.value slug}/{RevisionName.value name}{extension}"

      try
        let! download = service.GetBlobContainerClient("revisions").GetBlobClient(blob).DownloadContentAsync(ct)
        return Some(download.Value.Content.ToString())
      with :? RequestFailedException as error when notFound error ->
        return None
    }

  member _.GetPublishedPosts () = read "published"

  member _.GetWorkingPosts () = read "working"

  member _.GetPublishedBlob (slug : Slug) = readBlob "published" slug

  member _.GetWorkingBlob (slug : Slug) = readBlob "working" slug

  member _.GetMigratedPosts () : DependencyResult<Set<Slug>> =
    runDependencyWithLogging Dependency.BlobStorage logger $"{nameof PostStore}.GetMigratedPosts"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()

      try
        let! blobs =
          service.GetBlobContainerClient("published").GetBlobsAsync(BlobTraits.Metadata, BlobStates.None, null, ct)
          |> TaskSeq.toListAsync

        return
          blobs
          |> List.filter (fun blob ->
            blob.Name.EndsWith extension && not (blob.Name.Contains '/') && markOf blob.Metadata = Migrated)
          |> List.choose (fun blob -> Slug.create (blob.Name.Substring(0, blob.Name.Length - extension.Length)) |> Result.toOption)
          |> Set.ofList
      with :? RequestFailedException as error when error.ErrorCode = "ContainerNotFound" ->
        return Set.empty
    }

  member _.GetPostSlugs () : DependencyResult<Slug list> =
    cancellableTaskResult {
      let! published, working = CancellableTaskResult.parallelZip (listSlugs "published") (listSlugs "working")
      return List.append published working |> List.distinct
    }

  /// A failed condition gives <c>ConditionFailed</c> and leaves the blob as it was.
  member _.WriteWorkingBlob (slug : Slug) (text : string) (condition : WriteCondition) : DependencyResult<WriteOutcome> =
    runDependencyWithLogging Dependency.BlobStorage logger $"{nameof PostStore}.WriteWorkingBlob({slug})"
    <| writeBlob "working" (blobName slug) text NotMigrated (conditionsOf condition)

  /// A failed condition gives <c>ConditionFailed</c> and leaves the blob as it was.
  member _.WritePublishedBlob
    (slug : Slug)
    (text : string)
    (mark : MigratedMark)
    (condition : WriteCondition)
    : DependencyResult<WriteOutcome> =
    runDependencyWithLogging Dependency.BlobStorage logger $"{nameof PostStore}.WritePublishedBlob({slug})"
    <| writeBlob "published" (blobName slug) text mark (conditionsOf condition)

  member _.WriteRevisionBlob (slug : Slug) (time : DateTime) (text : string) : DependencyResult<WriteOutcome> =
    runDependencyWithLogging Dependency.BlobStorage logger $"{nameof PostStore}.WriteRevisionBlob({slug})"
    <| writeBlob "revisions" (revisionName slug time) text NotMigrated (conditionsOf MustNotExist)

  /// A revision that is already gone is not an error.
  member _.DeleteRevisionBlob (slug : Slug) (time : DateTime) : DependencyResult<unit> =
    runDependencyWithLogging Dependency.BlobStorage logger $"{nameof PostStore}.DeleteRevisionBlob({slug})"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()

      do!
        service.GetBlobContainerClient("revisions").GetBlobClient(revisionName slug time).DeleteIfExistsAsync(cancellationToken = ct)
        |> Task.ignore
    }
