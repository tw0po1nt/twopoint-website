namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Posts
open TwoPoint.Dependencies.Util
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Communication.Email
open Azure.Data.Tables
open Azure.Messaging.ServiceBus
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System.Net
open System.Threading

type UpdateCommentApprovalJson =
  { Approval: string option }
  
type UpdateCommentApproval(
  config: Config,
  emailClient: EmailClient,
  logger: ILogger<UpdateCommentApproval>,
  serviceBus: ServiceBusClient,
  tableServiceClient: TableServiceClient
) =

  [<Function("Admin-Posts-Comments-UpdateApproval")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "internal/posts/{slug}/comments/{commentId}")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    commentId : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.Comments.Approve"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User
    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let response = req.CreateResponse HttpStatusCode.OK
        logger.LogInformation("Processing 'Admin.Posts.Comments.Approve' request with slug '{slug}' and comment id '{commentId}'", slug, commentId)
        let validRedirectUris = config.ValidRedirectUris |> List.map _.Uri

        let! json = req.ReadFromJsonAsync<UpdateCommentApprovalJson> ct

        // Dependencies
        let postDependencies = PostDependencies(
          validRedirectUris,
          emailClient,
          config.Azure.EmailSender,
          tableServiceClient,
          SystemDependencies.live,
          logger
        )
        let updateCommentApproval = UpdateCommentApproval.withDependencies {
          new UpdateCommentApproval.Dependencies with
          
          member _.GetComment post commentId = postDependencies.GetComment post commentId
          member _.UpdateCommentApproval comment approval = postDependencies.UpdateCommentApproval comment approval
          member _.QueueReplyEmail post reply = ReplyEmailQueueClient.enqueue serviceBus logger op post reply
        }

        let approvalUpdate =
          { CommentApprovalUpdate.Dto.Post = CommentApprovalUpdate.Post slug
            CommentApprovalUpdate.Dto.CommentId = CommentApprovalUpdate.CommentId commentId
            CommentApprovalUpdate.Dto.Approval =
              json.Approval
              |> Option.map CommentApprovalUpdate.Approval
              |> Option.defaultValue (CommentApprovalUpdate.Approval "") }

        let! approvalUpdateResult = ct |> updateCommentApproval approvalUpdate
        let apiResponse, statusCode =
          approvalUpdateResult
          |> ActionResult.toApiResponse
            (fun _ -> None)
            (function
              | UpdateCommentApproval.Error.CommentNotFound commentId ->
                $"Comment '{commentId.ToString()}' not found for post '{slug}'", HttpStatusCode.NotFound
              | UpdateCommentApproval.Error.ChangeNotAllowed(current, requested) ->
                $"Comment is {current} and cannot become {requested}", HttpStatusCode.Conflict)

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
