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

type CreateDraftJson = { Title : string }

type CreatedDraftDto = { Slug : string; ETag : string }

type CreateDraft (
  logger : ILogger<CreateDraft>,
  postStoreClient: BlobServiceClient
) =

  [<Function("Admin-Posts-CreateDraft")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "internal/posts")>] req : HttpRequestData,
    context : FunctionContext,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.CreateDraft"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request", op)
        let response = req.CreateResponse HttpStatusCode.Created
        let! json = req.ReadFromJsonAsync<CreateDraftJson> ct
        let title = json |> Option.ofObj |> Option.bind (fun json -> Option.ofObj json.Title) |> Option.defaultValue ""

        let postStore = PostStore(postStoreClient, logger)
        let createDraft = CreateDraft.withDependencies {
          new CreateDraft.Dependencies with

          member _.GetPostSlugs () = postStore.GetPostSlugs()
          member _.WriteWorkingBlob slug text condition = postStore.WriteWorkingBlob slug text condition
        }

        let! result = createDraft { Title = title }

        let apiResponse, statusCode =
          match result with
          | Ok(CreateDraft.Event.Created(slug, eTag)) ->
            { Success = true; Message = None; Data = Some { Slug = slug.ToString(); ETag = eTag } }, HttpStatusCode.Created
          | Error(Logic(CreateDraft.Error.SlugTaken slug)) ->
            { Success = false; Message = Some $"Post '{slug}' exists"; Data = None }, HttpStatusCode.Conflict
          | Error actionError when actionError.IsValidation ->
            { Success = false; Message = Some(actionError.ToString()); Data = None }, HttpStatusCode.BadRequest
          | Error actionError ->
            { Success = false; Message = Some(actionError.ToString()); Data = None }, HttpStatusCode.InternalServerError

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
