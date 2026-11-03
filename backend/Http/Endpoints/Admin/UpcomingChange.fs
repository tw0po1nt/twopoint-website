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

/// A null `Edits` is a plain removal.
type UnpublishPostJson = { Edits : RemovalEditJson list }

/// `PublishDate` is null after an unpublish. `ETag` is the ETag of the working blob, or null when the post has no working blob.
/// Each warning is `comment-row`, `pending-edit`, `newer-text`, `production-rebuild`, `staging-rebuild`, `site-rebuild`,
/// `deploy-tracking`, `pending-date`, `post-row`, `revisions`, or `comments`.
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
    | RevisionsKept _ -> "revisions"
    | CommentsKept _ -> "comments"
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

module UnpublishMapping =

  let edits (json : UnpublishPostJson) =
    json |> Option.ofObj |> Option.bind (fun json -> Option.ofObj json.Edits) |> RemovalMapping.edits

  /// The unpublish published the pending edits and kept the post upcoming.
  let kept (slug : string) (edits : Slug list) (reason : Unpublish.KeptReason) (warnings : PublishSteps.Warning list) =
    let draftWritten = "The post now has a pending edit with no publish date. Reload the post before you continue."

    let because =
      match reason with
      | Unpublish.EditNotPublished(edit, failure) -> RemovalMapping.editNotPublished "unpublish" edit failure
      | Unpublish.WorkingBlobKept -> "Another tab saved the post during the unpublish.", HttpStatusCode.Conflict
      | Unpublish.WorkingWriteFailed _ -> "The write of the draft text failed.", HttpStatusCode.InternalServerError
      | Unpublish.PublishedBlobKept ->
        $"The published text of the post changed during the unpublish. {draftWritten}", HttpStatusCode.Conflict
      | Unpublish.PublishedDeleteFailed _ ->
        $"The delete of the published text failed. {draftWritten}", HttpStatusCode.InternalServerError

    RemovalMapping.kept "unpublish" slug edits because warnings

  let error (error : Unpublish.Error) =
    match error with
    | Unpublish.Refused refusal -> UpcomingMapping.refusal refusal
    | Unpublish.BreaksLinks problems ->
      $"The unpublish breaks a link in another post and is not done. {RemovalMapping.errors problems}", HttpStatusCode.UnprocessableEntity
    | Unpublish.EditsChanged ->
      "The pending edits that remove the links to the post changed. The admin did not unpublish the post. Check the unpublish again.",
      HttpStatusCode.Conflict

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
          let! json = req.ReadFromJsonAsync<UnpublishPostJson> ct
          let postStore = PostStore(postStoreClient, logger, texts)

          let postDependencies =
            PostDependencies(config.ValidRedirectUris |> List.map _.Uri, emailClient, config.Azure.EmailSender, tableServiceClient, logger)

          let rebuilder = SiteRebuilder(config, credential, environment, httpClientFactory, logger, tableServiceClient)
          let gate = PublishGateStore(config, images, parseCache, postStore)
          let unpublish = Unpublish.withDependencies (PostPublishing(gate, postStore, postDependencies, rebuilder))

          let! result =
            unpublish
              { Slug = slug
                IfMatch = SaveDraftHeaders.ifMatch precondition
                Edits = UnpublishMapping.edits json }

          let answer = ActionResult.toApiResponse (UpcomingMapping.change >> Some) UnpublishMapping.error

          let apiResponse, statusCode =
            match result with
            | Ok(Unpublish.Event.PostKept(edits, reason, _, warnings)) ->
              logger.LogWarning(
                "The unpublish of the post '{slug}' published the pending edits of {edits} and kept the post: {reason}",
                slug,
                RemovalMapping.names edits,
                reason
              )

              for warning in warnings do
                logger.LogWarning("The unpublish of the post '{slug}' kept the post, and a step failed: {warning}", slug, warning)

              let message, statusCode = UnpublishMapping.kept slug edits reason warnings
              ({ Success = false; Message = Some message; Data = None } : ApiResponse<UpcomingChangeDto>), statusCode
            | Ok(Unpublish.Event.Unpublished change) ->
              for warning in change.Warnings do
                logger.LogWarning("The post '{slug}' is unpublished, and a step after the write failed: {warning}", slug, warning)

              answer (Ok change)
            | Error error -> answer (Error error)

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
