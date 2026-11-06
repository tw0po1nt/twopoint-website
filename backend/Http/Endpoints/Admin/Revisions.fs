namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Posts
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Storage.Blobs
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System
open System.Net
open System.Threading

/// `Name` is the value to send to the restore. `Title` is null when the front matter of the revision has no title.
type RevisionDto =
  { Name : string
    PublishedAt : string
    Title : string }

type RestoreRevisionJson = { Revision : string }

type RestoredRevisionDto = { ETag : string }

module GetRevisionsQuery = TwoPoint.Core.Posts.Queries.GetRevisions

module RevisionMapping =

  let listing (revision : RevisionListing) : RevisionDto =
    { Name = RevisionName.value revision.Name
      PublishedAt = TwoPoint.Site.Render.Time.isoTime (DateTimeOffset revision.PublishedAt)
      Title = revision.Title |> Option.toObj }

  let error (error : RestoreRevision.Error) =
    match error with
    | RestoreRevision.Error.PostNotFound slug -> $"Post '{slug}' not found", HttpStatusCode.NotFound
    | RestoreRevision.Error.NotPublished slug ->
      $"'{slug}' is a draft. Only published posts have revisions", HttpStatusCode.Conflict
    | RestoreRevision.Error.RevisionNotFound(slug, name) -> $"The post '{slug}' has no revision '{name}'", HttpStatusCode.NotFound
    | RestoreRevision.Error.Stale -> "The post changed since you opened it", HttpStatusCode.PreconditionFailed

type GetRevisions (
  logger : ILogger<GetRevisions>,
  postStoreClient : BlobServiceClient,
  texts : BlobTextCache
) =

  [<Function("Admin-Posts-GetRevisions")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "internal/posts/{slug}/revisions")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.GetRevisions"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request with slug '{slug}'", op, slug)
        let response = req.CreateResponse HttpStatusCode.OK
        let postStore = PostStore(postStoreClient, logger, texts)

        let getRevisions = GetRevisionsQuery.withDependencies {
          new GetRevisionsQuery.Dependencies with

          member _.GetRevisionBlobs slug = postStore.GetRevisionBlobs slug
        }

        let! result = getRevisions slug

        let apiResponse, statusCode = result |> QueryResult.toApiResponse (List.map RevisionMapping.listing)

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )

type RestoreRevision (
  logger : ILogger<RestoreRevision>,
  postStoreClient : BlobServiceClient,
  texts : BlobTextCache
) =

  [<Function("Admin-Posts-RestoreRevision")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "internal/posts/{slug}/revisions/restore")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.RestoreRevision"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request with slug '{slug}'", op, slug)
        let response = req.CreateResponse HttpStatusCode.OK

        match SaveDraftHeaders.precondition req with
        | Missing -> return! PreconditionResponse.missingOrNone<RestoredRevisionDto> req
        | precondition ->
          let! json = req.ReadFromJsonAsync<RestoreRevisionJson> ct
          let revision = json |> Option.ofObj |> Option.bind (fun json -> Option.ofObj json.Revision) |> Option.defaultValue ""
          let postStore = PostStore(postStoreClient, logger, texts)

          let restore = RestoreRevision.withDependencies {
            new RestoreRevision.Dependencies with

            member _.GetPublishedBlob slug = postStore.GetPublishedBlob slug
            member _.GetWorkingBlob slug = postStore.GetWorkingBlob slug
            member _.GetRevisionBlob slug name = postStore.GetRevisionBlob slug name
            member _.WriteWorkingBlob slug text condition = postStore.WriteWorkingBlob slug text condition
          }

          let! result = restore { Slug = slug; Revision = revision; IfMatch = SaveDraftHeaders.ifMatch precondition }

          let apiResponse, statusCode =
            result
            |> ActionResult.toApiResponse
              (fun (RestoreRevision.Event.Restored eTag) -> Some { RestoredRevisionDto.ETag = eTag })
              RevisionMapping.error

          response.StatusCode <- statusCode
          do! response.WriteAsJsonAsync(apiResponse, ct)
          return response
      }
    )
