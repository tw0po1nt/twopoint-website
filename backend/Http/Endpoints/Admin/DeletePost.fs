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

/// `IfMatch` is the ETag of the working blob that the delete check gave.
type DeleteEditJson = { Slug : string; IfMatch : string }

/// A null `ConfirmSlug` confirms nothing. A null `Edits` is a plain delete.
type DeletePostJson = { ConfirmSlug : string; Edits : DeleteEditJson list }

type DeletedPostDto =
  { /// Null for a draft, which dispatches no rebuild.
    ProductionRun : Nullable<int64>
    /// The slugs of the posts whose pending edits the delete published first.
    Edits : string list
    /// Each warning is one of the warnings of `UpcomingChangeDto`, or `revisions` or `comments`.
    Warnings : string list }

type DeleteCheckEditDto = { Slug : string; Title : string; ETag : string }

/// `Outcome` is `plain`, `publish-and-delete`, or `blocked`. `Edits` are the pending edits that a publish and delete publishes.
/// `Problems` are the problems that block the delete.
type DeleteCheckDto =
  { Outcome : string
    Edits : DeleteCheckEditDto list
    Problems : ProblemDto list }

module DeleteMapping =

  let confirmation (json : DeletePostJson) =
    json |> Option.ofObj |> Option.bind (fun json -> Option.ofObj json.ConfirmSlug)

  let edits (json : DeletePostJson) : DeletePost.EditRef list =
    json
    |> Option.ofObj
    |> Option.bind (fun json -> Option.ofObj json.Edits)
    |> Option.defaultValue []
    |> List.map (fun edit -> { Slug = edit.Slug; IfMatch = edit.IfMatch })

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

  let names (slugs : Slug list) = slugs |> List.map (fun slug -> $"'{slug}'") |> String.concat ", "

  let keptWarning (warning : PublishSteps.Warning) =
    match warning with
    | PublishSteps.CommentRowNotWritten _ -> "The post table has no row for a published edit, so readers cannot comment on that post yet."
    | PublishSteps.PendingEditKept _ -> "A published edit keeps its pending edit. The published text is correct."
    | PublishSteps.NewerTextKept -> "Another tab saved newer text during the delete. That text is a pending edit."
    | PublishSteps.ProductionRebuildFailed _ -> "The production rebuild did not start. Select Rebuild site on the dashboard."
    | PublishSteps.StagingRebuildFailed _ -> "The staging rebuild did not start. Select Rebuild site on the dashboard."
    | PublishSteps.SiteNotRebuilt _ -> "The site rebuild did not start. Select Rebuild site on the dashboard."
    | PublishSteps.DeployNotTracked _ -> "The admin cannot show the deploy state of the published edits. The site still builds."

  /// The delete published the pending edits and kept the post.
  let kept (slug : string) (edits : Slug list) (reason : DeletePost.KeptReason) (warnings : PublishSteps.Warning list) =
    let because, statusCode =
      match reason with
      | DeletePost.EditNotPublished(edit, PublishSteps.WriteFailed _) ->
        $"The pending edit of the post '{edit}' did not publish.", HttpStatusCode.InternalServerError
      | DeletePost.EditNotPublished(edit, (PublishSteps.RevisionTaken | PublishSteps.PublishedConditionFailed)) ->
        $"The post '{edit}' changed during the delete, so its pending edit did not publish.", HttpStatusCode.Conflict
      | DeletePost.PublishedBlobKept -> "The published text of the post changed during the delete.", HttpStatusCode.Conflict
      | DeletePost.PublishedDeleteFailed _ -> "The delete of the published text failed.", HttpStatusCode.InternalServerError

    let message =
      $"The admin published the pending edits of the posts {names edits} and did not delete the post '{slug}'. {because}"

    String.concat " " (message :: List.map keptWarning warnings), statusCode

  let error (error : DeletePost.Error) =
    match error with
    | DeletePost.PostNotFound missing -> $"Post '{missing}' not found", HttpStatusCode.NotFound
    | DeletePost.WorkingBlobChanged -> "The post changed since you opened it", HttpStatusCode.PreconditionFailed
    | DeletePost.PublishedBlobChanged -> "The published text changed since you opened the post", HttpStatusCode.Conflict
    | DeletePost.SlugNotConfirmed slug -> $"Type the slug '{slug}' to delete the published post", HttpStatusCode.UnprocessableEntity
    | DeletePost.BreaksLinks problems ->
      $"If you delete the post, a link in another post breaks. The admin does not delete the post. {UpcomingMapping.errors problems}",
      HttpStatusCode.UnprocessableEntity
    | DeletePost.EditsChanged ->
      "The pending edits that remove the links to the post changed. The admin did not delete the post. Check the delete again.",
      HttpStatusCode.Conflict

  let check (outcome : DeleteCheck.Outcome) : DeleteCheckDto =
    match outcome with
    | DeleteCheck.PlainDelete -> { Outcome = "plain"; Edits = []; Problems = [] }
    | DeleteCheck.PublishAndDelete edits ->
      { Outcome = "publish-and-delete"
        Edits = edits |> List.map (fun edit -> { Slug = Slug.value edit.Slug; Title = edit.Title; ETag = edit.ETag })
        Problems = [] }
    | DeleteCheck.Blocked problems ->
      { Outcome = "blocked"
        Edits = []
        Problems = problems |> List.map ProblemMapping.problem }

  let checkError (error : CheckDelete.Error) =
    match error with
    | CheckDelete.PostNotFound missing -> $"Post '{missing}' not found", HttpStatusCode.NotFound
    | CheckDelete.NotPublished draft -> $"The post '{draft}' is a draft. A draft needs no delete check", HttpStatusCode.Conflict

type CheckDelete(
  config : Config,
  images : TwoPoint.Site.Render.IImageSize,
  logger : ILogger<CheckDelete>,
  parseCache : PublishedParseCache,
  postStoreClient : BlobServiceClient,
  texts : BlobTextCache
) =

  [<Function("Admin-Posts-CheckDelete")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "internal/posts/{slug}/delete/check")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.CheckDelete"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request with slug '{slug}'", op, slug)
        let response = req.CreateResponse HttpStatusCode.OK

        let gate = PublishGateStore(config, images, parseCache, PostStore(postStoreClient, logger, texts))
        let! result = CheckDelete.withDependencies gate { Slug = slug }

        let apiResponse, statusCode =
          result
          |> ActionResult.toApiResponse
            (fun (CheckDelete.Event.Checked outcome) -> Some(DeleteMapping.check outcome))
            DeleteMapping.checkError

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )

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

          let answer () =
            result
            |> ActionResult.toApiResponse
              (fun (DeletePost.Event.PostDeleted outcome) -> Some(DeleteMapping.deleted outcome))
              DeleteMapping.error

          let apiResponse, statusCode =
            match result with
            | Ok(DeletePost.Event.PostDeleted(DeletePost.PostKept(edits, reason, _, warnings) as outcome)) ->
              logger.LogWarning(
                "The delete of the post '{slug}' published the pending edits of {edits} and kept the post: {reason}",
                slug,
                DeleteMapping.names edits,
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

              answer ()
            | Error _ -> answer ()

          response.StatusCode <- statusCode
          do! response.WriteAsJsonAsync(apiResponse, ct)
          return response
      }
    )
