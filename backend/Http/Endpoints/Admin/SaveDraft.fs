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

type SaveDraftJson =
  { Title : string
    Excerpt : string
    Body : string
    /// Null leaves the details of the stored text as they are.
    Details : PostDetailsDto }

type SavedDraftDto = { ETag : string }

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

  let precondition (req : HttpRequestData) =
    match header req "If-Match", header req "If-None-Match" with
    | Some eTag, _ -> IfMatch eTag
    | None, Some "*" -> IfNoneMatchAny
    | None, _ -> Missing

type SaveDraft (
  logger : ILogger<SaveDraft>,
  postStoreClient: BlobServiceClient
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
          let json = json |> Option.ofObj |> Option.defaultValue { Title = ""; Excerpt = ""; Body = ""; Details = Unchecked.defaultof<PostDetailsDto> }

          let postStore = PostStore(postStoreClient, logger)
          let saveDraft = SaveDraft.withDependencies {
            new SaveDraft.Dependencies with

            member _.GetPublishedBlob slug = postStore.GetPublishedBlob slug
            member _.GetWorkingBlob slug = postStore.GetWorkingBlob slug
            member _.WriteWorkingBlob slug text condition = postStore.WriteWorkingBlob slug text condition
          }

          let! result =
            saveDraft
              { Slug = slug
                IfMatch = (match precondition with IfMatch eTag -> Some eTag | _ -> None)
                Title = text json.Title
                Excerpt = text json.Excerpt
                Body = text json.Body
                Details = json.Details |> Option.ofObj |> Option.map PostDetailsDto.toDetails }

          let apiResponse, statusCode =
            match result with
            | Ok(SaveDraft.Event.Saved eTag) -> { Success = true; Message = None; Data = Some { ETag = eTag } }, HttpStatusCode.OK
            | Error(Logic SaveDraft.Error.Stale) ->
              { Success = false; Message = Some "The post changed since you opened it"; Data = None }, HttpStatusCode.PreconditionFailed
            | Error(Logic(SaveDraft.Error.FrontMatterUnreadable reason)) ->
              { Success = false
                Message = Some $"The front matter of the stored post is not readable: {reason}"
                Data = None },
              HttpStatusCode.UnprocessableEntity
            | Error(Logic(SaveDraft.Error.PostNotFound slug)) ->
              { Success = false; Message = Some $"Post '{slug}' not found"; Data = None }, HttpStatusCode.NotFound
            | Error actionError when actionError.IsValidation ->
              { Success = false; Message = Some(actionError.ToString()); Data = None }, HttpStatusCode.BadRequest
            | Error actionError ->
              { Success = false; Message = Some(actionError.ToString()); Data = None }, HttpStatusCode.InternalServerError

          response.StatusCode <- statusCode
          do! response.WriteAsJsonAsync(apiResponse, ct)
          return response
      }
    )
