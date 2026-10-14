namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Http
open TwoPoint.Http.Endpoints
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Posts

open Azure.Communication.Email
open Azure.Data.Tables
open FirebaseAdmin.Messaging
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
  messaging : FirebaseMessaging,
  logger: ILogger<UpdateCommentApproval>,
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
          messaging,
          tableServiceClient,
          logger
        )
        let updateCommentApproval = UpdateCommentApproval.withDependencies {
          new UpdateCommentApproval.Dependencies with
          
          member _.GetCommentById commentId = postDependencies.GetCommentById commentId
          member _.UpdateCommenterStatus commenter status = postDependencies.UpdateCommenterStatus commenter status
          member _.UpdateCommentApproval comment approval = postDependencies.UpdateCommentApproval comment approval
        }

        let approvalUpdate =
          { CommentApprovalUpdate.Dto.CommentId = CommentApprovalUpdate.CommentId commentId
            CommentApprovalUpdate.Dto.Approval =
              json.Approval
              |> Option.map CommentApprovalUpdate.Approval
              |> Option.defaultValue (CommentApprovalUpdate.Approval "") }

        let! approvalUpdateResult = ct |> updateCommentApproval approvalUpdate
        let apiResponse, statusCode =
          match approvalUpdateResult with
          | Ok _ ->
            { Success = true; Message = None; Data = None }, HttpStatusCode.OK
          | Error (Logic updateCommentApprovalError) ->
            let errorMessage =
              match updateCommentApprovalError with
              | UpdateCommentApproval.Error.CommentNotFound commentId -> $"Comment '{commentId.ToString()}' not found for post '{slug}'"
            { Success = false; Message = Some errorMessage; Data = None }, HttpStatusCode.NotFound
          | Error actionError when actionError.IsValidation ->
            { Success = false;  Message = Some (actionError.ToString()); Data = None }, HttpStatusCode.BadRequest
          | Error actionError ->
            { Success = false; Message = Some (actionError.ToString()); Data = None }, HttpStatusCode.InternalServerError

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
