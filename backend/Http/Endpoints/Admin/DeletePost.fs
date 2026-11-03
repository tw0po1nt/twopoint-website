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

/// A null `ConfirmSlug` confirms nothing. A null `Edits` is a plain delete.
type DeletePostJson = { ConfirmSlug : string; Edits : RemovalEditJson list }

type DeletedPostDto =
  { /// Null for a draft, which dispatches no rebuild.
    ProductionRun : Nullable<int64>
    /// The slugs of the posts whose pending edits the delete published first.
    Edits : string list
    /// Each warning is one of the warnings of `UpcomingChangeDto`, or `revisions` or `comments`.
    Warnings : string list }

module DeleteMapping =

  let confirmation (json : DeletePostJson) =
    json |> Option.ofObj |> Option.bind (fun json -> Option.ofObj json.ConfirmSlug)

  let edits (json : DeletePostJson) =
    json |> Option.ofObj |> Option.bind (fun json -> Option.ofObj json.Edits) |> RemovalMapping.edits

  let warnings (outcome : DeletePost.Outcome) =
    match outcome with
    | DeletePost.DraftDeleted warnings
    | DeletePost.PublishedDeleted(_, _, warnings) -> warnings
    | DeletePost.PostKept(_, _, _, warnings) -> List.map Step warnings

  let deleted (outcome : DeletePost.Outcome) : DeletedPostDto =
    let edits, production =
      match outcome with
      | DeletePost.DraftDeleted _ -> [], None
      | DeletePost.PublishedDeleted(edits, production, _)
      | DeletePost.PostKept(edits, _, production, _) -> edits, Some production

    { ProductionRun = production |> Option.bind ProductionDispatch.runNumber |> Option.map _.Value |> Option.toNullable
      Edits = edits |> List.map Slug.value
      Warnings = warnings outcome |> List.map UpcomingMapping.warning }

  /// The delete published the pending edits and kept the post.
  let kept (slug : string) (edits : Slug list) (reason : DeletePost.KeptReason) (warnings : PublishSteps.Warning list) =
    let because =
      match reason with
      | DeletePost.EditNotPublished(edit, failure) -> RemovalMapping.editNotPublished "delete" edit failure
      | DeletePost.PublishedBlobKept -> "The published text of the post changed during the delete.", HttpStatusCode.Conflict
      | DeletePost.PublishedDeleteFailed _ -> "The delete of the published text failed.", HttpStatusCode.InternalServerError

    RemovalMapping.kept "delete" slug edits because warnings

  let error (error : DeletePost.Error) =
    match error with
    | DeletePost.PostNotFound missing -> $"Post '{missing}' not found", HttpStatusCode.NotFound
    | DeletePost.WorkingBlobChanged -> "The post changed since you opened it", HttpStatusCode.PreconditionFailed
    | DeletePost.PublishedBlobChanged -> "The published text changed since you opened the post", HttpStatusCode.Conflict
    | DeletePost.SlugNotConfirmed slug -> $"Type the slug '{slug}' to delete the published post", HttpStatusCode.UnprocessableEntity
    | DeletePost.BreaksLinks problems ->
      $"If you delete the post, a link in another post breaks. The admin does not delete the post. {RemovalMapping.errors problems}",
      HttpStatusCode.UnprocessableEntity
    | DeletePost.EditsChanged ->
      "The pending edits that remove the links to the post changed. The admin did not delete the post. Check the delete again.",
      HttpStatusCode.Conflict

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
            deletePost
              { Slug = slug
                IfMatch = SaveDraftHeaders.ifMatch precondition
                ConfirmSlug = DeleteMapping.confirmation json
                Edits = DeleteMapping.edits json }

          let answer = ActionResult.toApiResponse (DeleteMapping.deleted >> Some) DeleteMapping.error

          let apiResponse, statusCode =
            match result with
            | Ok(DeletePost.Event.PostDeleted(DeletePost.PostKept(edits, reason, _, warnings) as outcome)) ->
              logger.LogWarning(
                "The delete of the post '{slug}' published the pending edits of {edits} and kept the post: {reason}",
                slug,
                RemovalMapping.names edits,
                reason
              )

              for warning in warnings do
                logger.LogWarning("The delete of the post '{slug}' kept the post, and a step failed: {warning}", slug, warning)

              let message, statusCode = DeleteMapping.kept slug edits reason warnings

              ({ Success = false; Message = Some message; Data = Some(DeleteMapping.deleted outcome) } : ApiResponse<DeletedPostDto>),
              statusCode
            | Ok(DeletePost.Event.PostDeleted outcome) ->
              for warning in DeleteMapping.warnings outcome do
                logger.LogWarning("The post '{slug}' is deleted, and a step after the delete failed: {warning}", slug, warning)

              answer (Ok outcome)
            | Error error -> answer (Error error)

          response.StatusCode <- statusCode
          do! response.WriteAsJsonAsync(apiResponse, ct)
          return response
      }
    )
