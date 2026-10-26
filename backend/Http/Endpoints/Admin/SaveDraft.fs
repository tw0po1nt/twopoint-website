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

type SaveDraftJson =
  { Title : string
    Excerpt : string
    Body : string
    /// Null leaves the details of the stored text as they are.
    Details : Nullable<PostDetailsDto> }

/// `Kind` is `found` or `unavailable`. `Unavailable` means that the draft is saved and the check did not run.
type CheckDto =
  { Kind : string
    Problems : ProblemDto list }

type SavedDraftDto = { ETag : string; Check : CheckDto }

/// The precondition of a save: the ETag in `If-Match`, or `If-None-Match: *` for a post with no working blob.
type SavePrecondition =
  | IfMatch of string
  | IfNoneMatchAny
  | Missing

module SaveDraftHeaders =

  let private header (req : HttpRequestData) (name : string) =
    match req.Headers.TryGetValues name with
    | true, values -> values |> Seq.tryHead
    | _ -> None

  let ifMatch (precondition : SavePrecondition) =
    match precondition with
    | IfMatch eTag -> Some eTag
    | IfNoneMatchAny
    | Missing -> None

  let precondition (req : HttpRequestData) =
    match header req "If-Match", header req "If-None-Match" with
    | Some eTag, _ -> IfMatch eTag
    | None, Some "*" -> IfNoneMatchAny
    | None, _ -> Missing

type SaveDraft (
  config : Config,
  logger : ILogger<SaveDraft>,
  postStoreClient: BlobServiceClient,
  images : TwoPoint.Site.Render.IImageSize,
  parseCache : PublishedParseCache,
  texts : BlobTextCache
) =

  [<Function("Admin-Posts-SaveDraft")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "internal/posts/{slug}")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.SaveDraft"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request with slug '{slug}'", op, slug)
        let response = req.CreateResponse HttpStatusCode.OK

        match SaveDraftHeaders.precondition req with
        | Missing ->
          let apiResponse : ApiResponse<SavedDraftDto> =
            { Success = false; Message = Some "Send If-Match with the ETag, or If-None-Match: *"; Data = None }

          response.StatusCode <- HttpStatusCode.PreconditionRequired
          do! response.WriteAsJsonAsync(apiResponse, ct)
          return response
        | precondition ->
          let! json = req.ReadFromJsonAsync<SaveDraftJson> ct
          let text (value : string) = value |> Option.ofObj |> Option.defaultValue ""
          let json = json |> Option.ofObj |> Option.defaultValue { Title = ""; Excerpt = ""; Body = ""; Details = Nullable() }

          let postStore = PostStore(postStoreClient, logger, texts)
          let saveDraft = SaveDraft.withDependencies {
            new SaveDraft.Dependencies with

            member _.GetPublishedBlob slug = postStore.GetPublishedBlob slug
            member _.GetWorkingBlob slug = postStore.GetWorkingBlob slug
            member _.WriteWorkingBlob slug text condition = postStore.WriteWorkingBlob slug text condition
            member _.Stage = config.Stage
            member _.Images = images
            member _.GetUtcNow () = System.DateTime.UtcNow
            member _.GetPublishedPosts () = postStore.GetPublishedPosts()
            member _.GetSeriesRecords () = postStore.GetSeriesRecords()
            member _.ParsePublished = parseCache.Parse
          }

          let! result =
            saveDraft
              { Slug = slug
                IfMatch = (match precondition with IfMatch eTag -> Some eTag | _ -> None)
                Title = text json.Title
                Excerpt = text json.Excerpt
                Body = text json.Body
                Details = json.Details |> Option.ofNullable |> Option.map PostDetailsDto.toDetails }

          let apiResponse, statusCode =
            result
            |> ActionResult.toApiResponse
              (fun (SaveDraft.Event.Saved(eTag, check)) ->
                match check with
                | PostCheck.Problems problems ->
                  Some
                    { ETag = eTag
                      Check = { Kind = "found"; Problems = problems |> List.map ProblemMapping.problem } }
                | PostCheck.Unavailable reason ->
                  logger.LogWarning("The check of the post '{slug}' did not run: {reason}", slug, reason)
                  Some { ETag = eTag; Check = { Kind = "unavailable"; Problems = [] } })
              (function
                | SaveDraft.Error.Stale -> "The post changed since you opened it", HttpStatusCode.PreconditionFailed
                | SaveDraft.Error.FrontMatterUnreadable reason ->
                  $"The front matter of the stored post is not readable: {reason}", HttpStatusCode.UnprocessableEntity
                | SaveDraft.Error.PostNotFound slug -> $"Post '{slug}' not found", HttpStatusCode.NotFound)

          response.StatusCode <- statusCode
          do! response.WriteAsJsonAsync(apiResponse, ct)
          return response
      }
    )
