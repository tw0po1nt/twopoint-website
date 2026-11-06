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

/// `IfMatch` is the ETag of the working blob that the removal check gave.
type RemovalEditJson = { Slug : string; IfMatch : string }

type RemovalCheckEditDto = { Slug : string; Title : string; ETag : string }

/// `Outcome` is `plain`, `publish-and-remove`, or `blocked`. `Edits` are the pending edits that a publish and remove publishes.
/// `Problems` are the problems that block the removal.
type RemovalCheckDto =
  { Outcome : string
    Edits : RemovalCheckEditDto list
    Problems : ProblemDto list }

module RemovalMapping =

  /// No list is a plain removal.
  let edits (json : RemovalEditJson list option) : RemovalSteps.EditRef list =
    json
    |> Option.defaultValue []
    |> List.map (fun edit -> { Slug = edit.Slug; IfMatch = edit.IfMatch })

  let names (slugs : Slug list) = slugs |> List.map (fun slug -> $"'{slug}'") |> String.concat ", "

  /// `action` is the name of the removal in the text, `delete` or `unpublish`.
  let keptWarning (action : string) (warning : PublishSteps.Warning) =
    match warning with
    | PublishSteps.CommentRowNotWritten _ -> "Comments aren't open yet on one of the published edits (its post-table row is missing)."
    | PublishSteps.PendingEditKept _ -> "A published edit is live, but a leftover pending edit wasn't cleared. Use 'Discard changes' to remove it."
    | PublishSteps.NewerTextKept -> $"Edits saved in another tab during the {action} are kept as a pending edit."
    | PublishSteps.ProductionRebuildFailed _ -> "Couldn't start the production rebuild. Click 'Rebuild site' on the dashboard."
    | PublishSteps.StagingRebuildFailed _ -> "Couldn't start the staging rebuild. Click 'Rebuild site' on the dashboard."
    | PublishSteps.SiteNotRebuilt _ -> "Couldn't start the rebuild. Click 'Rebuild site' on the dashboard."
    | PublishSteps.DeployNotTracked _ -> "Can't track the deploy status of the published edits. The site is still rebuilding."

  let editNotPublished (action : string) (edit : Slug) (failure : PublishSteps.WriteFailure) =
    match failure with
    | PublishSteps.WriteFailed _ -> $"Couldn't publish the pending edit of '{edit}'.", HttpStatusCode.InternalServerError
    | PublishSteps.RevisionTaken
    | PublishSteps.PublishedConditionFailed ->
      $"'{edit}' changed during the {action}, so its pending edit wasn't published.", HttpStatusCode.Conflict

  /// The removal published the pending edits of `edits` and kept the post. `because` is the reason and its status.
  let kept
    (action : string)
    (slug : string)
    (edits : Slug list)
    (because : string, statusCode : HttpStatusCode)
    (warnings : PublishSteps.Warning list)
    =
    let message =
      $"Published the pending edits of {names edits}, but couldn't {action} '{slug}'. {because}"

    String.concat " " (message :: List.map (keptWarning action) warnings), statusCode

  let errors (problems : TwoPoint.Site.Render.Problem list) =
    problems |> List.map _.Message |> String.concat " "

  let check (outcome : RemovalCheck.Outcome) : RemovalCheckDto =
    match outcome with
    | RemovalCheck.PlainRemoval -> { Outcome = "plain"; Edits = []; Problems = [] }
    | RemovalCheck.PublishAndRemove edits ->
      { Outcome = "publish-and-remove"
        Edits = edits |> List.map (fun edit -> { Slug = Slug.value edit.Slug; Title = edit.Title; ETag = edit.ETag })
        Problems = [] }
    | RemovalCheck.Blocked problems ->
      { Outcome = "blocked"
        Edits = []
        Problems = problems |> List.map ProblemMapping.problem }

  let checkError (error : CheckRemoval.Error) =
    match error with
    | CheckRemoval.PostNotFound missing -> $"Post '{missing}' not found", HttpStatusCode.NotFound
    | CheckRemoval.NotPublished draft -> $"The post '{draft}' is a draft. A draft needs no removal check", HttpStatusCode.Conflict

type CheckRemoval(
  config : Config,
  images : TwoPoint.Site.Render.IImageSize,
  logger : ILogger<CheckRemoval>,
  parseCache : PublishedParseCache,
  postStoreClient : BlobServiceClient,
  texts : BlobTextCache
) =

  [<Function("Admin-Posts-CheckRemoval")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "internal/posts/{slug}/removal/check")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.CheckRemoval"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request with slug '{slug}'", op, slug)
        let response = req.CreateResponse HttpStatusCode.OK

        let gate = PublishGateStore(config, images, parseCache, PostStore(postStoreClient, logger, texts))
        let! result = CheckRemoval.withDependencies gate { Slug = slug }

        let apiResponse, statusCode =
          result
          |> ActionResult.toApiResponse
            (fun (CheckRemoval.Event.Checked outcome) -> Some(RemovalMapping.check outcome))
            RemovalMapping.checkError

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
