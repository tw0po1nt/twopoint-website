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
open FsToolkit.ErrorHandling
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

open System
open System.Net
open System.Net.Http
open System.Threading

/// A null `PublishDate` publishes now.
type PublishPostJson = { PublishDate : string }

type PublishedPostDto =
  { PublishDate : string
    State : string
    ProductionRun : Nullable<int64>
    /// Each is `comment-row`, `pending-edit`, `newer-text`, `production-rebuild`, `staging-rebuild`, `site-rebuild`, or `deploy-tracking`.
    Warnings : string list }

type PublishCheckDto = { Problems : ProblemDto list }

module PreconditionResponse =

  let missingWith<'T> (message : string) (req : HttpRequestData) : CancellableTask<HttpResponseData> =
    cancellableTask {
      let! ct = CancellableTask.getCancellationToken()
      let apiResponse : ApiResponse<'T> = { Success = false; Message = Some message; Data = None }
      let response = req.CreateResponse HttpStatusCode.PreconditionRequired
      do! response.WriteAsJsonAsync(apiResponse, ct)
      return response
    }

  let missing<'T> (req : HttpRequestData) = missingWith<'T> "Send If-Match with the ETag" req

  let missingOrNone<'T> (req : HttpRequestData) =
    missingWith<'T> "Send If-Match with the ETag, or If-None-Match: * when the post has no working blob" req

module PublishMapping =

  let choice (json : PublishPostJson) =
    match json |> Option.ofObj |> Option.bind (fun json -> Option.ofObj json.PublishDate) with
    | Some date when date <> "" -> ChooseDate date
    | _ -> ChooseNow

  let warning (warning : PublishSteps.Warning) =
    match warning with
    | PublishSteps.CommentRowNotWritten _ -> "comment-row"
    | PublishSteps.PendingEditKept _ -> "pending-edit"
    | PublishSteps.NewerTextKept -> "newer-text"
    | PublishSteps.ProductionRebuildFailed _ -> "production-rebuild"
    | PublishSteps.StagingRebuildFailed _ -> "staging-rebuild"
    | PublishSteps.SiteNotRebuilt _ -> "site-rebuild"
    | PublishSteps.DeployNotTracked _ -> "deploy-tracking"

  let published (outcome : PublishSteps.Outcome) : PublishedPostDto =
    { PublishDate = TwoPoint.Site.Render.Time.isoTime (DateTimeOffset outcome.PublishDate)
      State =
        match outcome.State with
        | PostState.Upcoming -> "upcoming"
        | PostState.Published -> "published"
        | PostState.Draft -> "draft"
      ProductionRun =
        ProductionDispatch.runNumber outcome.Production |> Option.map _.Value |> Option.toNullable
      Warnings = outcome.Warnings |> List.map warning }

  let refusal (refusal : PublishGate.Refusal) =
    match refusal with
    | PublishGate.WorkingBlobChanged -> "The post changed since you opened it", HttpStatusCode.PreconditionFailed
    | PublishGate.PostNotFound missing -> $"Post '{missing}' not found", HttpStatusCode.NotFound
    | PublishGate.AlreadyPublished published -> $"The post '{published}' is already published", HttpStatusCode.Conflict
    | PublishGate.DateInPast -> "The publish date is in the past", HttpStatusCode.UnprocessableEntity
    | PublishGate.FrontMatterUnreadable reason ->
      $"The front matter of the stored post is not readable: {reason}", HttpStatusCode.UnprocessableEntity

type PublishGateStore(
  config : Config,
  images : TwoPoint.Site.Render.IImageSize,
  parseCache : PublishedParseCache,
  postStore : PostStore
) =

  interface PublishGate.Dependencies with
    member _.Stage = config.Stage
    member _.Images = images
    member _.GetUtcNow () = DateTime.UtcNow
    member _.GetPublishedBlob slug = postStore.GetPublishedBlob slug
    member _.GetWorkingBlob slug = postStore.GetWorkingBlob slug
    member _.GetPublishedPosts () = postStore.GetPublishedPosts()
    member _.GetSeriesRecords () = postStore.GetSeriesRecords()
    member _.ParsePublished = parseCache.Parse

type PostPublishing(
  gate : PublishGate.Dependencies,
  postStore : PostStore,
  postDependencies : PostDependencies,
  rebuilder : SiteRebuilder
) =

  interface PublishPost.Dependencies with
    member _.Stage = gate.Stage
    member _.Images = gate.Images
    member _.GetUtcNow () = gate.GetUtcNow()
    member _.GetPublishedBlob slug = gate.GetPublishedBlob slug
    member _.GetWorkingBlob slug = gate.GetWorkingBlob slug
    member _.GetPublishedPosts () = gate.GetPublishedPosts()
    member _.GetSeriesRecords () = gate.GetSeriesRecords()
    member _.ParsePublished = gate.ParsePublished
    member _.WriteRevisionBlob slug time text = postStore.WriteRevisionBlob slug time text
    member _.DeleteRevisionBlob slug time = postStore.DeleteRevisionBlob slug time
    member _.WritePublishedBlob slug text mark condition = postStore.WritePublishedBlob slug text mark condition
    member _.UpsertPostRow post = postDependencies.UpsertPost post |> CancellableTaskResult.map ignore
    member _.DeleteWorkingBlob slug eTag = postStore.DeleteWorkingBlob slug eTag
    member _.DispatchSite () = rebuilder.Dispatch()
    member _.RecordChange change = rebuilder.RecordChange change

  interface UpcomingGate.Dependencies with
    member _.WriteWorkingBlob slug text condition = postStore.WriteWorkingBlob slug text condition

  interface Unpublish.Dependencies with
    member _.DeletePublishedBlob slug eTag = postStore.DeletePublishedBlob slug eTag
    member _.DeletePostRow slug = postDependencies.DeletePost slug

  interface DeletePost.Dependencies with
    member _.DeletePublishedBlob slug eTag = postStore.DeletePublishedBlob slug eTag
    member _.DeleteRevisionBlobs slug = postStore.DeleteRevisionBlobs slug
    member _.DeletePostComments slug = postDependencies.DeleteCommentsOfPost slug
    member _.DeletePostRow slug = postDependencies.DeletePost slug
    member _.HasPostRecords slug = postDependencies.HasPostRecords slug

type CheckPublish(
  config : Config,
  images : TwoPoint.Site.Render.IImageSize,
  logger : ILogger<CheckPublish>,
  parseCache : PublishedParseCache,
  postStoreClient : BlobServiceClient,
  texts : BlobTextCache
) =

  [<Function("Admin-Posts-CheckPublish")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "internal/posts/{slug}/publish/check")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.CheckPublish"
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
          let! json = req.ReadFromJsonAsync<PublishPostJson> ct

          let gate = PublishGateStore(config, images, parseCache, PostStore(postStoreClient, logger, texts))
          let checkPublish = CheckPublish.withDependencies gate

          let! result = checkPublish { Slug = slug; IfMatch = Some eTag; PublishTime = PublishMapping.choice json }

          let apiResponse, statusCode =
            result
            |> ActionResult.toApiResponse
              (fun (CheckPublish.Event.Checked problems) ->
                Some { Problems = problems |> List.map ProblemMapping.problem })
              (fun (CheckPublish.Error.Refused refusal) -> PublishMapping.refusal refusal)

          response.StatusCode <- statusCode
          do! response.WriteAsJsonAsync(apiResponse, ct)
          return response
        | IfNoneMatchAny
        | Missing -> return! PreconditionResponse.missing<PublishCheckDto> req
      }
    )

type PublishPost(
  config : Config,
  credential : TokenCredential,
  emailClient : EmailClient,
  environment : IHostEnvironment,
  httpClientFactory : IHttpClientFactory,
  images : TwoPoint.Site.Render.IImageSize,
  logger : ILogger<PublishPost>,
  parseCache : PublishedParseCache,
  postStoreClient : BlobServiceClient,
  tableServiceClient : TableServiceClient,
  texts : BlobTextCache
) =

  [<Function("Admin-Posts-Publish")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "internal/posts/{slug}/publish")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.Publish"
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
          let! json = req.ReadFromJsonAsync<PublishPostJson> ct

          let postStore = PostStore(postStoreClient, logger, texts)

          let postDependencies =
            PostDependencies(config.ValidRedirectUris |> List.map _.Uri, emailClient, config.Azure.EmailSender, tableServiceClient, logger)

          let rebuilder = SiteRebuilder(config, credential, environment, httpClientFactory, logger, tableServiceClient)
          let gate = PublishGateStore(config, images, parseCache, postStore)
          let publishPost = PublishPost.withDependencies (PostPublishing(gate, postStore, postDependencies, rebuilder))

          let! result = publishPost { Slug = slug; IfMatch = Some eTag; PublishTime = PublishMapping.choice json }

          match result with
          | Ok(PublishPost.Event.Published outcome) ->
            for warning in outcome.Warnings do
              logger.LogWarning("The post '{slug}' is published, and a step after the write failed: {warning}", slug, warning)
          | Error _ -> ()

          let apiResponse, statusCode =
            result
            |> ActionResult.toApiResponse
              (fun (PublishPost.Event.Published outcome) -> Some(PublishMapping.published outcome))
              (function
                | PublishPost.Error.Refused refusal -> PublishMapping.refusal refusal
                | PublishPost.Error.RevisionExists _ ->
                  "Another publish of the post is in progress", HttpStatusCode.Conflict
                | PublishPost.Error.HasErrors problems ->
                  let messages = problems |> List.map _.Message |> String.concat " "
                  $"The post has errors and is not published. {messages}", HttpStatusCode.UnprocessableEntity)

          response.StatusCode <- statusCode
          do! response.WriteAsJsonAsync(apiResponse, ct)
          return response
        | IfNoneMatchAny
        | Missing -> return! PreconditionResponse.missing<PublishedPostDto> req
      }
    )
