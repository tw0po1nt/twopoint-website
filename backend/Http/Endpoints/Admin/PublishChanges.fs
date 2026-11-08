namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Posts
open TwoPoint.Dependencies.Util
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

open System.Net
open System.Net.Http
open System.Threading

/// A null `UpdateDate` leaves the update date of the text as it is.
type PublishChangesJson = { UpdateDate : string }

module PendingMapping =

  let updateDate (json : PublishChangesJson) =
    json |> Option.ofObj |> Option.bind (fun json -> Option.ofObj json.UpdateDate) |> Option.filter (fun date -> date <> "")

  let refusal (refusal : PendingGate.Refusal) =
    match refusal with
    | PendingGate.WorkingBlobChanged -> "The post changed since you opened it", HttpStatusCode.PreconditionFailed
    | PendingGate.PublishedBlobChanged -> "The published text changed since you opened the post", HttpStatusCode.Conflict
    | PendingGate.PostNotFound missing -> $"Post '{missing}' not found", HttpStatusCode.NotFound
    | PendingGate.NotPublished draft -> $"The post '{draft}' is a draft", HttpStatusCode.Conflict
    | PendingGate.NoPendingEdit published -> $"'{published}' has no changes to publish", HttpStatusCode.Conflict
    | PendingGate.NoPublishDate undated -> $"The live version of '{undated}' has no publish date", HttpStatusCode.UnprocessableEntity
    | PendingGate.FrontMatterUnreadable reason ->
      $"Couldn't read the saved post's front matter: {reason}", HttpStatusCode.UnprocessableEntity

type CheckChanges(
  config : Config,
  images : TwoPoint.Site.Render.IImageSize,
  logger : ILogger<CheckChanges>,
  parseCache : PublishedParseCache,
  postStoreClient : BlobServiceClient,
  texts : BlobTextCache
) =

  [<Function("Admin-Posts-CheckChanges")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "internal/posts/{slug}/changes/publish/check")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.CheckChanges"
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
          let! json = req.ReadFromJsonAsync<PublishChangesJson> ct

          let gate = PublishGateStore(config, images, parseCache, PostStore(postStoreClient, logger, texts))
          let checkChanges = CheckChanges.withDependencies gate

          let! result = checkChanges ({ Slug = slug; IfMatch = Some eTag; UpdateDate = PendingMapping.updateDate json } : PendingGate.Dto)

          let apiResponse, statusCode =
            result
            |> ActionResult.toApiResponse
              (fun (CheckChanges.Event.Checked problems) -> Some { Problems = problems |> List.map ProblemMapping.problem })
              (fun (CheckChanges.Error.Refused refusal) -> PendingMapping.refusal refusal)

          response.StatusCode <- statusCode
          do! response.WriteAsJsonAsync(apiResponse, ct)
          return response
        | IfNoneMatchAny
        | Missing -> return! PreconditionResponse.missing<PublishCheckDto> req
      }
    )

type PublishChanges(
  config : Config,
  credential : TokenCredential,
  emailClient : EmailClient,
  environment : IHostEnvironment,
  httpClientFactory : IHttpClientFactory,
  images : TwoPoint.Site.Render.IImageSize,
  logger : ILogger<PublishChanges>,
  parseCache : PublishedParseCache,
  postStoreClient : BlobServiceClient,
  tableServiceClient : TableServiceClient,
  texts : BlobTextCache
) =

  [<Function("Admin-Posts-PublishChanges")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "internal/posts/{slug}/changes/publish")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.PublishChanges"
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
          let! json = req.ReadFromJsonAsync<PublishChangesJson> ct

          let postStore = PostStore(postStoreClient, logger, texts)

          let postDependencies =
            PostDependencies(config.ValidRedirectUris |> List.map _.Uri, emailClient, config.Azure.EmailSender, tableServiceClient, SystemDependencies.live, logger)

          let rebuilder = SiteRebuilder(config, credential, environment, httpClientFactory, logger, tableServiceClient)
          let gate = PublishGateStore(config, images, parseCache, postStore)
          let publishChanges = PublishChanges.withDependencies (PostPublishing(gate, postStore, postDependencies, rebuilder))

          let! result = publishChanges ({ Slug = slug; IfMatch = Some eTag; UpdateDate = PendingMapping.updateDate json } : PendingGate.Dto)

          match result with
          | Ok(PublishChanges.Event.Published outcome) ->
            for warning in outcome.Warnings do
              logger.LogWarning("The changes of the post '{slug}' are published, and a step after the write failed: {warning}", slug, warning)
          | Error _ -> ()

          let apiResponse, statusCode =
            result
            |> ActionResult.toApiResponse
              (fun (PublishChanges.Event.Published outcome) -> Some(PublishMapping.published outcome))
              (function
                | PublishChanges.Error.Refused refusal -> PendingMapping.refusal refusal
                | PublishChanges.Error.RevisionExists _ ->
                  "Another publish of the post is in progress", HttpStatusCode.Conflict
                | PublishChanges.Error.HasErrors problems ->
                  let messages = problems |> List.map _.Message |> String.concat " "
                  $"The post has errors, so the changes weren't published. {messages}", HttpStatusCode.UnprocessableEntity)

          response.StatusCode <- statusCode
          do! response.WriteAsJsonAsync(apiResponse, ct)
          return response
        | IfNoneMatchAny
        | Missing -> return! PreconditionResponse.missing<PublishedPostDto> req
      }
    )

type DiscardChanges(
  logger : ILogger<DiscardChanges>,
  postStoreClient : BlobServiceClient,
  texts : BlobTextCache
) =

  [<Function("Admin-Posts-DiscardChanges")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "internal/posts/{slug}/changes/discard")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.DiscardChanges"
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
          let postStore = PostStore(postStoreClient, logger, texts)

          let discardChanges =
            DiscardChanges.withDependencies {
              new DiscardChanges.Dependencies with

              member _.GetPublishedBlob slug = postStore.GetPublishedBlob slug
              member _.GetWorkingBlob slug = postStore.GetWorkingBlob slug
              member _.DeleteWorkingBlob slug eTag = postStore.DeleteWorkingBlob slug eTag
            }

          let! result = discardChanges { Slug = slug; IfMatch = Some eTag }

          let apiResponse, statusCode =
            result
            |> ActionResult.toApiResponse
              (fun DiscardChanges.Event.Discarded -> None)
              (fun (DiscardChanges.Error.Refused refusal) -> PendingMapping.refusal refusal)

          response.StatusCode <- statusCode
          do! response.WriteAsJsonAsync(apiResponse, ct)
          return response
        | IfNoneMatchAny
        | Missing -> return! PreconditionResponse.missing<obj> req
      }
    )
