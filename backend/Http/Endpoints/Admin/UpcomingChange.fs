namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Deploys
open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Posts
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Communication.Email
open Azure.Core
open Azure.Data.Tables
open Azure.Storage.Blobs
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

open System
open System.Net
open System.Net.Http
open System.Threading

type ChangeDateJson = { PublishDate : string }

/// `PublishDate` is null after an unpublish. `ETag` is the ETag of the working blob, or null when the post has no working blob.
/// Each warning is `comment-row`, `pending-edit`, `newer-text`, `production-rebuild`, `staging-rebuild`, `site-rebuild`,
/// `deploy-tracking`, `pending-date`, or `post-row`.
type UpcomingChangeDto =
  { PublishDate : string
    State : string
    ETag : string
    ProductionRun : Nullable<int64>
    Warnings : string list }

module UpcomingMapping =

  let warning (warning : ChangeWarning) =
    match warning with
    | Step step -> PublishMapping.warning step
    | PostRowKept _ -> "post-row"
    | PendingDateNotUpdated _
    | PendingDateUnreadable _ -> "pending-date"

  let change (change : UpcomingChange) : UpcomingChangeDto =
    { PublishDate = change.PublishDate |> Option.map (DateTimeOffset >> TwoPoint.Site.Render.Time.isoTime) |> Option.toObj
      State =
        match change.State with
        | PostState.Upcoming -> "upcoming"
        | PostState.Published -> "published"
        | PostState.Draft -> "draft"
      ETag = change.WorkingETag |> Option.toObj
      ProductionRun = ProductionDispatch.runNumber change.Production |> Option.map _.Value |> Option.toNullable
      Warnings = change.Warnings |> List.map warning }

  let refusal (refusal : UpcomingGate.Refusal) =
    match refusal with
    | UpcomingGate.Gate gate -> PendingMapping.refusal gate
    | UpcomingGate.NotUpcoming live -> $"The post '{live}' is not upcoming. Its publish date is in the past", HttpStatusCode.Conflict

  let errors (problems : TwoPoint.Site.Render.Problem list) =
    problems |> List.map _.Message |> String.concat " "

type UnpublishPost(
  config : Config,
  credential : TokenCredential,
  emailClient : EmailClient,
  environment : IHostEnvironment,
  httpClientFactory : IHttpClientFactory,
  images : TwoPoint.Site.Render.IImageSize,
  logger : ILogger<UnpublishPost>,
  parseCache : PublishedParseCache,
  postStoreClient : BlobServiceClient,
  tableServiceClient : TableServiceClient,
  texts : BlobTextCache
) =

  [<Function("Admin-Posts-Unpublish")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "internal/posts/{slug}/unpublish")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.Unpublish"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request with slug '{slug}'", op, slug)
        let response = req.CreateResponse HttpStatusCode.OK

        match SaveDraftHeaders.precondition req with
        | Missing -> return! PreconditionResponse.missingOrNone<UpcomingChangeDto> req
        | precondition ->
          let postStore = PostStore(postStoreClient, logger, texts)

          let postDependencies =
            PostDependencies(config.ValidRedirectUris |> List.map _.Uri, emailClient, config.Azure.EmailSender, tableServiceClient, logger)

          let rebuilder = SiteRebuilder(config, credential, environment, httpClientFactory, logger, tableServiceClient)
          let gate = PublishGateStore(config, images, parseCache, postStore)
          let unpublish = Unpublish.withDependencies (PostPublishing(gate, postStore, postDependencies, rebuilder))

          let! result = unpublish { Slug = slug; IfMatch = SaveDraftHeaders.ifMatch precondition }

          match result with
          | Ok(Unpublish.Event.Unpublished change) ->
            for warning in change.Warnings do
              logger.LogWarning("The post '{slug}' is unpublished, and a step after the write failed: {warning}", slug, warning)
          | Error _ -> ()

          let apiResponse, statusCode =
            result
            |> ActionResult.toApiResponse
              (fun (Unpublish.Event.Unpublished change) -> Some(UpcomingMapping.change change))
              (function
                | Unpublish.Error.Refused refusal -> UpcomingMapping.refusal refusal
                | Unpublish.Error.HasErrors problems ->
                  $"The unpublish breaks a link in another post and is not done. {UpcomingMapping.errors problems}", HttpStatusCode.UnprocessableEntity)

          response.StatusCode <- statusCode
          do! response.WriteAsJsonAsync(apiResponse, ct)
          return response
      }
    )

type ChangePostDate(
  config : Config,
  credential : TokenCredential,
  emailClient : EmailClient,
  environment : IHostEnvironment,
  httpClientFactory : IHttpClientFactory,
  images : TwoPoint.Site.Render.IImageSize,
  logger : ILogger<ChangePostDate>,
  parseCache : PublishedParseCache,
  postStoreClient : BlobServiceClient,
  tableServiceClient : TableServiceClient,
  texts : BlobTextCache
) =

  [<Function("Admin-Posts-ChangeDate")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "internal/posts/{slug}/date")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.ChangeDate"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request with slug '{slug}'", op, slug)
        let response = req.CreateResponse HttpStatusCode.OK

        match SaveDraftHeaders.precondition req with
        | Missing -> return! PreconditionResponse.missingOrNone<UpcomingChangeDto> req
        | precondition ->
          let! json = req.ReadFromJsonAsync<ChangeDateJson> ct

          let publishDate = json |> Option.ofObj |> Option.bind (fun json -> Option.ofObj json.PublishDate) |> Option.defaultValue ""

          let postStore = PostStore(postStoreClient, logger, texts)

          let postDependencies =
            PostDependencies(config.ValidRedirectUris |> List.map _.Uri, emailClient, config.Azure.EmailSender, tableServiceClient, logger)

          let rebuilder = SiteRebuilder(config, credential, environment, httpClientFactory, logger, tableServiceClient)
          let gate = PublishGateStore(config, images, parseCache, postStore)
          let changeDate = ChangeDate.withDependencies (PostPublishing(gate, postStore, postDependencies, rebuilder))

          let! result = changeDate { Slug = slug; IfMatch = SaveDraftHeaders.ifMatch precondition; PublishDate = publishDate }

          match result with
          | Ok(ChangeDate.Event.DateChanged change) ->
            for warning in change.Warnings do
              logger.LogWarning("The date of the post '{slug}' is changed, and a step after the write failed: {warning}", slug, warning)
          | Error _ -> ()

          let apiResponse, statusCode =
            result
            |> ActionResult.toApiResponse
              (fun (ChangeDate.Event.DateChanged change) -> Some(UpcomingMapping.change change))
              (fun (ChangeDate.Error.Refused refusal) -> UpcomingMapping.refusal refusal)

          response.StatusCode <- statusCode
          do! response.WriteAsJsonAsync(apiResponse, ct)
          return response
      }
    )
