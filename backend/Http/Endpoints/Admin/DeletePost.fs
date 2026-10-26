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

/// A null `ConfirmSlug` confirms nothing.
type DeletePostJson = { ConfirmSlug : string }

type DeletedPostDto =
  { /// Null for a draft, which dispatches no rebuild.
    ProductionRun : Nullable<int64>
    /// Each warning is one of the warnings of `UpcomingChangeDto`, or `revisions` or `comments`.
    Warnings : string list }

module DeleteMapping =

  let confirmation (json : DeletePostJson) =
    json |> Option.ofObj |> Option.bind (fun json -> Option.ofObj json.ConfirmSlug)

  let parts (outcome : DeletePost.Outcome) =
    match outcome with
    | DeletePost.DraftDeleted warnings -> None, warnings
    | DeletePost.PublishedDeleted(production, warnings) -> Some production, warnings

  let deleted (outcome : DeletePost.Outcome) : DeletedPostDto =
    let production, warnings = parts outcome

    { ProductionRun = production |> Option.bind ProductionDispatch.runNumber |> Option.map _.Value |> Option.toNullable
      Warnings = warnings |> List.map UpcomingMapping.warning }

  let error (error : DeletePost.Error) =
    match error with
    | DeletePost.PostNotFound missing -> $"Post '{missing}' not found", HttpStatusCode.NotFound
    | DeletePost.WorkingBlobChanged -> "The post changed since you opened it", HttpStatusCode.PreconditionFailed
    | DeletePost.PublishedBlobChanged -> "The published text changed since you opened the post", HttpStatusCode.Conflict
    | DeletePost.SlugNotConfirmed slug -> $"Type the slug '{slug}' to delete the published post", HttpStatusCode.UnprocessableEntity
    | DeletePost.BreaksLinks problems ->
      $"If you delete the post, a link in another post breaks. The admin does not delete the post. {UpcomingMapping.errors problems}",
      HttpStatusCode.UnprocessableEntity

type DeletePost(
  config : Config,
  credential : TokenCredential,
  emailClient : EmailClient,
  environment : IHostEnvironment,
  httpClientFactory : IHttpClientFactory,
  images : TwoPoint.Site.Render.IImageSize,
  logger : ILogger<DeletePost>,
  parseCache : PublishedParseCache,
  postStoreClient : BlobServiceClient,
  tableServiceClient : TableServiceClient,
  texts : BlobTextCache
) =

  [<Function("Admin-Posts-Delete")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "internal/posts/{slug}")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.Delete"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request with slug '{slug}'", op, slug)
        let response = req.CreateResponse HttpStatusCode.OK

        match SaveDraftHeaders.precondition req with
        | Missing -> return! PreconditionResponse.missingOrNone<DeletedPostDto> req
        | precondition ->
          let! json = req.ReadFromJsonAsync<DeletePostJson> ct

          let postStore = PostStore(postStoreClient, logger, texts)

          let postDependencies =
            PostDependencies(config.ValidRedirectUris |> List.map _.Uri, emailClient, config.Azure.EmailSender, tableServiceClient, logger)

          let rebuilder = SiteRebuilder(config, credential, environment, httpClientFactory, logger, tableServiceClient)
          let gate = PublishGateStore(config, images, parseCache, postStore)
          let deletePost = DeletePost.withDependencies (PostPublishing(gate, postStore, postDependencies, rebuilder))

          let! result =
            deletePost { Slug = slug; IfMatch = SaveDraftHeaders.ifMatch precondition; ConfirmSlug = DeleteMapping.confirmation json }

          match result with
          | Ok(DeletePost.Event.PostDeleted outcome) ->
            for warning in snd (DeleteMapping.parts outcome) do
              logger.LogWarning("The post '{slug}' is deleted, and a step after the delete failed: {warning}", slug, warning)
          | Error _ -> ()

          let apiResponse, statusCode =
            result
            |> ActionResult.toApiResponse
              (fun (DeletePost.Event.PostDeleted outcome) -> Some(DeleteMapping.deleted outcome))
              DeleteMapping.error

          response.StatusCode <- statusCode
          do! response.WriteAsJsonAsync(apiResponse, ct)
          return response
      }
    )
