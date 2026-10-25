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
  postStoreClient: BlobServiceClient
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
          let postStore = PostStore(postStoreClient, logger)

          let changeSlug = ChangeSlug.withDependencies {
            new ChangeSlug.Dependencies with

            member _.GetPostSlugs () = postStore.GetPostSlugs()
            member _.GetPublishedBlob slug = postStore.GetPublishedBlob slug
            member _.GetWorkingBlob slug = postStore.GetWorkingBlob slug
            member _.WriteWorkingBlob slug text condition = postStore.WriteWorkingBlob slug text condition
            member _.DeleteWorkingBlob slug condition = postStore.DeleteWorkingBlob slug condition
          }

          let! result = changeSlug { Slug = slug; IfMatch = Some eTag; NewSlug = newSlug }

          let apiResponse, statusCode =
            match result with
            | Ok(ChangeSlug.Event.Changed(changed, eTag)) ->
              { Success = true; Message = None; Data = Some { Slug = changed.ToString(); ETag = eTag } }, HttpStatusCode.OK
            | Error(Logic ChangeSlug.Error.Stale) ->
              { Success = false; Message = Some "The post changed since you opened it"; Data = None }, HttpStatusCode.PreconditionFailed
            | Error(Logic(ChangeSlug.Error.PostNotFound missing)) ->
              { Success = false; Message = Some $"Post '{missing}' not found"; Data = None }, HttpStatusCode.NotFound
            | Error(Logic(ChangeSlug.Error.PostPublished published)) ->
              { Success = false; Message = Some $"The slug of the published post '{published}' is fixed"; Data = None },
              HttpStatusCode.Conflict
            | Error(Logic(ChangeSlug.Error.SlugTaken taken)) ->
              { Success = false; Message = Some $"Another post has the slug '{taken}'"; Data = None }, HttpStatusCode.Conflict
            | Error actionError when actionError.IsValidation ->
              { Success = false; Message = Some(actionError.ToString()); Data = None }, HttpStatusCode.BadRequest
            | Error actionError ->
              { Success = false; Message = Some(actionError.ToString()); Data = None }, HttpStatusCode.InternalServerError

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
