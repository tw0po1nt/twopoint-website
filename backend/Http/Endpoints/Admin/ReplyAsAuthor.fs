namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Posts
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Communication.Email
open Azure.Data.Tables
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System.Net
open System.Threading

type AuthorReplyJson = { Content : string option }

/// `Approved` is the ID of each comment that the reply approved.
type AuthorReplyDto =
  { Reply : AdminCommentDto
    Approved : string list }

[<RequireQualifiedAccess>]
module AuthorReplyMapping =

  let refused (slug : string) (commentId : string) (reason : AuthorReplyRefusal) =
    match reason with
    | AuthorReplyRefusal.ReplyToRejected ->
      "This comment is rejected, so you can't reply to it.", HttpStatusCode.Conflict
    | AuthorReplyRefusal.ReplyToMissing -> $"Comment '{commentId}' not found for post '{slug}'", HttpStatusCode.NotFound

  let replied (ReplyAsAuthor.Event.Replied (reply, approved)) =
    { Reply = reply.ToAdminDto()
      Approved = approved |> List.map (_.Id >> string) }

type ReplyAsAuthor(
  config: Config,
  emailClient: EmailClient,
  logger: ILogger<ReplyAsAuthor>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Admin-Posts-Comments-Reply")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "internal/posts/{slug}/comments/{commentId}/replies")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    commentId : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.Comments.Reply"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User
    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let response = req.CreateResponse HttpStatusCode.OK
        logger.LogInformation("Processing '{op}' request with slug '{slug}' and comment id '{commentId}'", op, slug, commentId)
        let validRedirectUris = config.ValidRedirectUris |> List.map _.Uri

        let! json = req.ReadFromJsonAsync<AuthorReplyJson> ct

        // Dependencies
        let postDependencies = PostDependencies(
          validRedirectUris,
          emailClient,
          config.Azure.EmailSender,
          tableServiceClient,
          logger
        )
        let replyAsAuthor = ReplyAsAuthor.withDependencies {
          new ReplyAsAuthor.Dependencies with

          member _.GetPostedCommentById commentId = postDependencies.GetPostedCommentById commentId
          member _.UpdateCommentApproval comment approval = postDependencies.UpdateCommentApproval comment approval
          member _.CreateAuthorReply post topLevel content approval =
            postDependencies.CreateAuthorReply post topLevel content approval
          member _.Stage = config.Stage
        }

        let reply =
          { AuthorReply.Dto.Post = slug
            AuthorReply.Dto.ReplyTo = commentId
            AuthorReply.Dto.Content = json |> Option.ofObj |> Option.bind _.Content |> Option.defaultValue "" }

        let! replyResult = ct |> replyAsAuthor reply
        let apiResponse, statusCode =
          replyResult
          |> ActionResult.toApiResponse
            (AuthorReplyMapping.replied >> Some)
            (fun (ReplyAsAuthor.Error.Refused reason) -> AuthorReplyMapping.refused slug commentId reason)

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
