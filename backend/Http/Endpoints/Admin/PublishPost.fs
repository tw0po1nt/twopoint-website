namespace TwoPoint.Http.Endpoints.Admin

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
open System.Globalization
open System.Net
open System.Net.Http
open System.Threading

/// A null `PublishDate` publishes now.
type PublishPostJson = { PublishDate : string }

type PublishedPostDto =
  { PublishDate : string
    State : string
    /// Each is `comment-row`, `pending-edit`, or `site-rebuild`.
    Warnings : string list }

type PublishCheckDto = { Problems : ProblemDto list }

module PublishMapping =

  let time (json : PublishPostJson) =
    match json |> Option.ofObj |> Option.bind (fun json -> Option.ofObj json.PublishDate) with
    | Some date when date <> "" -> On date
    | _ -> Now

  let warning (warning : PublishPost.Warning) =
    match warning with
    | PublishPost.CommentRowNotWritten _ -> "comment-row"
    | PublishPost.PendingEditKept _ -> "pending-edit"
    | PublishPost.SiteNotRebuilt _ -> "site-rebuild"

  let published (outcome : PublishPost.Outcome) : PublishedPostDto =
    { PublishDate = outcome.PublishDate.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
      State =
        match outcome.State with
        | PostState.Upcoming -> "upcoming"
        | PostState.Published -> "published"
        | PostState.Draft -> "draft"
      Warnings = outcome.Warnings |> List.map warning }

  let refusal (refusal : PublishGate.Refusal) =
    match refusal with
    | PublishGate.Stale -> "The post changed since you opened it", HttpStatusCode.PreconditionFailed
    | PublishGate.PostNotFound missing -> $"Post '{missing}' not found", HttpStatusCode.NotFound
    | PublishGate.AlreadyPublished published -> $"The post '{published}' is already published", HttpStatusCode.Conflict
    | PublishGate.DateInPast -> "The publish date is in the past", HttpStatusCode.UnprocessableEntity
    | PublishGate.FrontMatterUnreadable reason ->
      $"The front matter of the stored post is not readable: {reason}", HttpStatusCode.UnprocessableEntity

type PostPublishing(
  config : Config,
  images : TwoPoint.Site.Render.IImageSize,
  parseCache : PublishedParseCache,
  postStore : PostStore,
  postDependencies : PostDependencies,
  rebuilder : SiteRebuilder
) =

  interface PublishPost.Dependencies with
    member _.Stage = config.Stage
    member _.Images = images
    member _.GetUtcNow () = DateTime.UtcNow
    member _.GetPublishedBlob slug = postStore.GetPublishedBlob slug
    member _.GetWorkingBlob slug = postStore.GetWorkingBlob slug
    member _.GetPublishedPosts () = postStore.GetPublishedPosts()
    member _.GetSeriesRecords () = postStore.GetSeriesRecords()
    member _.ParsePublished = parseCache.Parse
    member _.WriteRevisionBlob slug time text = postStore.WriteRevisionBlob slug time text
    member _.WritePublishedBlob slug text condition = postStore.WritePublishedBlob slug text condition
    member _.UpsertPostRow post = postDependencies.UpsertPost post |> CancellableTaskResult.map ignore
    member _.DeleteWorkingBlob slug eTag = postStore.DeleteWorkingBlob slug eTag
    member _.RebuildSite () = rebuilder.RebuildAsDependency()

type CheckPublish(
  config : Config,
  credential : TokenCredential,
  emailClient : EmailClient,
  environment : IHostEnvironment,
  httpClientFactory : IHttpClientFactory,
  images : TwoPoint.Site.Render.IImageSize,
  logger : ILogger<CheckPublish>,
  parseCache : PublishedParseCache,
  postStoreClient : BlobServiceClient,
  tableServiceClient : TableServiceClient,
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

          let postDependencies =
            PostDependencies(config.ValidRedirectUris |> List.map _.Uri, emailClient, config.Azure.EmailSender, tableServiceClient, logger)

          let rebuilder = SiteRebuilder(config, credential, environment, httpClientFactory, logger, tableServiceClient)
          let publishing = PostPublishing(config, images, parseCache, PostStore(postStoreClient, logger, texts), postDependencies, rebuilder)
          let checkPublish = CheckPublish.withDependencies publishing

          let! result = checkPublish { Slug = slug; IfMatch = Some eTag; PublishTime = PublishMapping.time json }

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
        | Missing ->
          let apiResponse : ApiResponse<PublishCheckDto> =
            { Success = false; Message = Some "Send If-Match with the ETag"; Data = None }

          response.StatusCode <- HttpStatusCode.PreconditionRequired
          do! response.WriteAsJsonAsync(apiResponse, ct)
          return response
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

          let postDependencies =
            PostDependencies(config.ValidRedirectUris |> List.map _.Uri, emailClient, config.Azure.EmailSender, tableServiceClient, logger)

          let rebuilder = SiteRebuilder(config, credential, environment, httpClientFactory, logger, tableServiceClient)
          let publishing = PostPublishing(config, images, parseCache, PostStore(postStoreClient, logger, texts), postDependencies, rebuilder)
          let publishPost = PublishPost.withDependencies publishing

          let! result = publishPost { Slug = slug; IfMatch = Some eTag; PublishTime = PublishMapping.time json }

          let apiResponse, statusCode =
            result
            |> ActionResult.toApiResponse
              (fun (PublishPost.Event.Published outcome) ->
                for warning in outcome.Warnings do
                  logger.LogWarning("The post '{slug}' is published, and a step after the write failed: {warning}", slug, warning)

                Some(PublishMapping.published outcome))
              (function
                | PublishPost.Error.Refused refusal -> PublishMapping.refusal refusal
                | PublishPost.Error.HasErrors problems ->
                  let messages = problems |> List.map _.Message |> String.concat " "
                  $"The post has errors and is not published. {messages}", HttpStatusCode.UnprocessableEntity)

          response.StatusCode <- statusCode
          do! response.WriteAsJsonAsync(apiResponse, ct)
          return response
        | IfNoneMatchAny
        | Missing ->
          let apiResponse : ApiResponse<PublishedPostDto> =
            { Success = false; Message = Some "Send If-Match with the ETag"; Data = None }

          response.StatusCode <- HttpStatusCode.PreconditionRequired
          do! response.WriteAsJsonAsync(apiResponse, ct)
          return response
      }
    )
