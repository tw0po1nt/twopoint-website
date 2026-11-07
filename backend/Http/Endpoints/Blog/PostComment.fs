namespace TwoPoint.Http.Endpoints.Blog

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Posts
open TwoPoint.Dependencies.Util
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Communication.Email
open Azure.Data.Tables
open Azure.Messaging.ServiceBus
open FsToolkit.ErrorHandling
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System
open System.Net
open System.Text.Json
open System.Threading

/// `ReplyTo` is the ID of the top-level comment of a reply.
type NewCommentJson =
  { ValidationId : string option
    Comment : string option
    ReplyTo : string option }

type PostedCommentDto =
  { Id : string
    CreatedDate : DateTime
    Commenter : string
    Content : string
    Approval : string
    ReplyTo : string option
    Writer : string }

module PostedCommentDto =
  let ofComment (comment : RenderedComment) =
    let answer = PublicComment.ofRendered comment
    { Id = answer.Id
      CreatedDate = answer.CreatedDate
      Commenter = answer.Commenter
      Content = answer.Content
      Approval = comment.Comment.Status.Approval.ToString()
      ReplyTo = answer.ReplyTo
      Writer = answer.Writer }

[<RequireQualifiedAccess>]
module PostCommentMapping =

  let replyRefused (reason : ReplyRefusal) =
    let message =
      match reason with
      | ReplyRefusal.ReplyToReply -> "You can reply only to a top-level comment."
      | ReplyRefusal.ReplyToNew -> "This comment is waiting for approval, so you can't reply to it yet."
      | ReplyRefusal.ReplyToRejected -> "This comment isn't on the post anymore, so you can't reply to it."
      | ReplyRefusal.ReplyToMissing -> "Couldn't find the comment that you're replying to."
      | ReplyRefusal.PostUpcoming -> "This post isn't published yet, so you can't reply to its comments."

    message, HttpStatusCode.Conflict

/// An older Comments widget reads a `data` that is not null as an invalid answer, so the comment goes beside it.
type PostCommentResponse =
  { Success : bool
    Message : string option
    Data : unit option
    Comment : PostedCommentDto option }

type PostComment (
  config: Config,
  emailClient: EmailClient,
  logger: ILogger<PostComment>,
  serviceBus: ServiceBusClient,
  tableServiceClient: TableServiceClient
) =

  let queueNewCommentNotice (commentId : CommentId) =
    runDependencyWithLogging Dependency.ServiceBus logger $"{nameof PostComment}.QueueNewCommentNotice"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      use sender = serviceBus.CreateSender NewCommentQueue.Name
      let json = JsonSerializer.Serialize { NewCommentMessage.CommentId = commentId.ToString() }
      do! sender.SendMessageAsync(ServiceBusMessage json, ct)
    }

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
      tableServiceClient,
      logger
    )
    let postComment = PostComment.withDependencies {
      new PostComment.Dependencies with
      
      member this.GetPostBySlug slug = postDependencies.GetPostBySlug slug
      member this.GetUtcNow () = DateTime.UtcNow
      member this.GetPostedCommentById commentId = postDependencies.GetPostedCommentById commentId
      member this.GetCommenterByValidationId validationId = postDependencies.GetCommenterByValidationId validationId
      member this.CreateComment post commenter newComment topLevel approval =
        postDependencies.CreateComment post commenter newComment topLevel approval
      member this.QueueNewCommentNotice commentId = queueNewCommentNotice commentId
      member this.Stage = config.Stage
    }

    let newComment =
      { NewComment.Dto.Post = NewComment.Post slug
        NewComment.Dto.ValidationId = json.ValidationId |> Option.defaultValue "" |> NewComment.ValidationId
        NewComment.Dto.Comment = json.Comment |> Option.map NewComment.Comment |> Option.defaultValue (NewComment.Comment "")
        NewComment.Dto.ReplyTo = json.ReplyTo |> Option.map NewComment.ReplyTo }

    let! postCommentResult = ct |> postComment newComment
    let answer success message comment =
      { Success = success; Message = message; Data = None; Comment = comment }
    let apiResponse, statusCode =
      match postCommentResult with
      | Ok (PostComment.Event.Posted (_, comment)) ->
        answer true None (Some (PostedCommentDto.ofComment comment)), HttpStatusCode.OK
      | Error (Logic (PostComment.Error.Banned _)) ->
        answer true None None, HttpStatusCode.OK
      | Error (Logic (PostComment.Error.ReplyRefused reason)) ->
        let message, statusCode = PostCommentMapping.replyRefused reason
        answer false (Some message) None, statusCode
      | Error (Logic postCommentError) ->
        let errorMessage =
          match postCommentError with
          | PostComment.Error.NotFound(PostComment.Data.Post slug) -> $"Post '{slug.ToString()}' not found"
          | PostComment.Error.PostUpcoming post -> $"Post '{post.Id.ToString()}' not found"
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
