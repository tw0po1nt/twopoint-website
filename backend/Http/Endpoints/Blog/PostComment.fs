namespace TwoPoint.Http.Endpoints.Blog

open TwoPoint.Http
open TwoPoint.Http.Endpoints
open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Posts

open Azure.Communication.Email
open Azure.Data.Tables
open FirebaseAdmin.Messaging
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System
open System.Net
open System.Threading

type NewCommentJson =
  { ValidationId : string option
    Comment : string option }

type PostedCommentDto =
  { Id : string
    CreatedDate : DateTime
    Commenter : string
    Content : string
    Approval : string }

module PostedCommentDto =
  let ofComment (comment : Comment) =
    let dto = comment.ToDto()
    { Id = dto.Id
      CreatedDate = dto.CreatedDate
      Commenter = dto.Commenter
      Content = dto.Content
      Approval = comment.Status.Approval.ToString() }

/// An older Comments widget reads a `data` that is not null as an invalid answer, so the comment goes beside it.
type PostCommentResponse =
  { Success : bool
    Message : string option
    Data : unit option
    Comment : PostedCommentDto option }

type PostComment (
  config: Config,
  emailClient: EmailClient,
  messaging : FirebaseMessaging,
  logger: ILogger<PostComment>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Blog-Posts-PostComment")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "blog/posts/{slug}/comments")>] req : HttpRequestData,
    slug : string,
    ct : CancellationToken
  ) = task {
    let response = req.CreateResponse HttpStatusCode.OK
    logger.LogInformation("Processing 'Blog.Posts.PostComment' request with slug '{slug}'", slug)
    let validRedirectUris = config.ValidRedirectUris |> List.map _.Uri

    let! json = req.ReadFromJsonAsync<NewCommentJson> ct

    // Dependencies
    let postDependencies = PostDependencies(
      validRedirectUris,
      emailClient,
      config.Azure.EmailSender,
      messaging,
      tableServiceClient,
      logger
    )
    let postComment = PostComment.withDependencies {
      new PostComment.Dependencies with
      
      member this.GetPostBySlug slug = postDependencies.GetPostBySlug slug
      member this.GetCommenterByValidationId validationId = postDependencies.GetCommenterByValidationId validationId
      member this.CreateComment post commenter newComment approval =
        postDependencies.CreateComment post commenter newComment approval
    }

    let newComment =
      { NewComment.Dto.Post = NewComment.Post slug
        NewComment.Dto.ValidationId = json.ValidationId |> Option.defaultValue "" |> NewComment.ValidationId
        NewComment.Dto.Comment = json.Comment |> Option.map NewComment.Comment |> Option.defaultValue (NewComment.Comment "") }

    let! postCommentResult = ct |> postComment newComment
    let answer success message comment =
      { Success = success; Message = message; Data = None; Comment = comment }
    let apiResponse, statusCode =
      match postCommentResult with
      | Ok (PostComment.Event.Posted (_, comment)) ->
        answer true None (Some (PostedCommentDto.ofComment comment)), HttpStatusCode.OK
      | Error (Logic (PostComment.Error.Banned _)) ->
        answer true None None, HttpStatusCode.OK
      | Error (Logic postCommentError) ->
        let errorMessage =
          match postCommentError with
          | PostComment.Error.NotFound(PostComment.Data.Post slug) -> $"Post '{slug.ToString()}' not found"
          | PostComment.Error.NotFound(PostComment.Data.Commenter validationId) -> $"Commenter '{validationId.ToString()}' not found"
          | _ -> "Not found"
        answer false (Some errorMessage) None, HttpStatusCode.NotFound
      | Error actionError when actionError.IsValidation ->
        answer false (Some (actionError.ToString())) None, HttpStatusCode.BadRequest
      | Error actionError ->
        answer false (Some (actionError.ToString())) None, HttpStatusCode.InternalServerError

    response.StatusCode <- statusCode
    do! response.WriteAsJsonAsync(apiResponse, ct)
    return response
  }
