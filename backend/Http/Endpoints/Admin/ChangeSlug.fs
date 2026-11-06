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

open System.Net
open System.Threading

type ChangeSlugJson = { Slug : string }

type ChangedSlugDto = { Slug : string; ETag : string }

type ChangeSlug (
  logger : ILogger<ChangeSlug>,
  postStoreClient: BlobServiceClient,
  texts : BlobTextCache
) =

  [<Function("Admin-Posts-ChangeSlug")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "internal/posts/{slug}/slug")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.ChangeSlug"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request with slug '{slug}'", op, slug)
        let response = req.CreateResponse HttpStatusCode.OK

        match SaveDraftHeaders.precondition req with
        | IfMatch eTag ->
          let! json = req.ReadFromJsonAsync<ChangeSlugJson> ct
          let newSlug = json |> Option.ofObj |> Option.bind (fun json -> Option.ofObj json.Slug) |> Option.defaultValue ""
          let postStore = PostStore(postStoreClient, logger, texts)

          let changeSlug = ChangeSlug.withDependencies {
            new ChangeSlug.Dependencies with

            member _.GetPostSlugs () = postStore.GetPostSlugs()
            member _.GetPublishedBlob slug = postStore.GetPublishedBlob slug
            member _.GetWorkingBlob slug = postStore.GetWorkingBlob slug
            member _.WriteWorkingBlob slug text condition = postStore.WriteWorkingBlob slug text condition
            member _.DeleteWorkingBlob slug eTag = postStore.DeleteWorkingBlob slug eTag
          }

          let! result = changeSlug { Slug = slug; IfMatch = Some eTag; NewSlug = newSlug }

          let apiResponse, statusCode =
            result
            |> ActionResult.toApiResponse
              (fun (ChangeSlug.Event.Changed(changed, eTag)) -> Some { Slug = changed.ToString(); ETag = eTag })
              (function
                | ChangeSlug.Error.Stale -> "The post changed since you opened it", HttpStatusCode.PreconditionFailed
                | ChangeSlug.Error.PostNotFound missing -> $"Post '{missing}' not found", HttpStatusCode.NotFound
                | ChangeSlug.Error.PostPublished published ->
                  "You can't change the slug of a published post", HttpStatusCode.Conflict
                | ChangeSlug.Error.SlugTaken taken -> $"Another post already uses the slug '{taken}'", HttpStatusCode.Conflict)

          response.StatusCode <- statusCode
          do! response.WriteAsJsonAsync(apiResponse, ct)
          return response
        | IfNoneMatchAny
        | Missing ->
          let apiResponse : ApiResponse<ChangedSlugDto> =
            { Success = false; Message = Some "Send If-Match with the ETag"; Data = None }

          response.StatusCode <- HttpStatusCode.PreconditionRequired
          do! response.WriteAsJsonAsync(apiResponse, ct)
          return response
      }
    )
