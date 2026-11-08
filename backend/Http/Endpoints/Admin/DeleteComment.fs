namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Http
open TwoPoint.Http.Endpoints
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Posts

open Azure.Communication.Email
open Azure.Data.Tables
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System.Net
open System.Threading

type DeleteComment(
  config: Config,
  emailClient: EmailClient,
  logger: ILogger<UpdateCommentApproval>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Admin-Posts-Comments-Delete")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "internal/posts/{slug}/comments/{commentId}")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    commentId : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.Comments.Delete"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User
    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let response = req.CreateResponse HttpStatusCode.OK
        logger.LogInformation("Processing 'Admin.Posts.Comments.Delete' request with slug '{slug}' and comment id '{commentId}'", slug, commentId)
        let validRedirectUris = config.ValidRedirectUris |> List.map _.Uri

        // Dependencies
        let postDependencies = PostDependencies(
          validRedirectUris,
          emailClient,
          config.Azure.EmailSender,
          tableServiceClient,
          logger
        )
        let deleteComment = DeleteComment.withDependencies {
          new DeleteComment.Dependencies with
          
          member _.GetPostedComment post commentId = postDependencies.GetPostedComment post commentId
          member _.GetRepliesOf topLevels = postDependencies.GetRepliesOf topLevels
          member _.DeleteComment comment = postDependencies.DeleteComment comment
        }

        let deletion =
          { CommentDeletion.Dto.Post = CommentDeletion.Post slug
            CommentDeletion.Dto.CommentId = CommentDeletion.CommentId commentId }

        let! deletionResult = ct |> deleteComment deletion

        match deletionResult with
        | Ok (DeleteComment.Event.Deleted (deletedId, others)) ->
          logger.LogInformation("Deleted comment '{commentId}' with {others} replies", deletedId.ToString(), others)
        | Error _ -> ()
        let apiResponse, statusCode =
          deletionResult
          |> ActionResult.toApiResponse
            (fun _ -> None)
            (fun (DeleteComment.Error.CommentNotFound commentId) ->
              $"Comment '{commentId.ToString()}' not found for post '{slug}'", HttpStatusCode.NotFound)

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
