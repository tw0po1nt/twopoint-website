namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Posts.Logic
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Communication.Email
open Azure.Data.Tables
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System.Net
open System.Security.Claims
open System.Threading

type RegisterForNotificationsJson =
  { Token: string }

type RegisterForNotifications(
  config: Config,
  emailClient: EmailClient,
  logger: ILogger<UpdateCommentApproval>,
  tableServiceClient: TableServiceClient
) =
  
  [<Function("Admin-Notifications-Register")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "internal/notifications")>] req : HttpRequestData,
    claimsPrincipal : ClaimsPrincipal | null,
    ct : CancellationToken
  ) =
    let op = "Admin.Notifications.Register"
    let claimsPrincipal = claimsPrincipal |> Option.ofObj
    ct |> (
      Auth.runIfAuthorized config logger req claimsPrincipal op
      <| cancellableTask {
        let response = req.CreateResponse HttpStatusCode.OK
        logger.LogInformation("Processing '{op}' request", op)
        
        let! json = req.ReadFromJsonAsync<RegisterForNotificationsJson>(ct)
        logger.LogWarning("Token: {token}", json.Token)
    
        // Dependencies
        // let postDependencies = PostDependencies.live validRedirectUris emailClient config.Azure.EmailSender tableServiceClient logger
        // let postActions = PostActions.withDependencies postDependencies
        //
        // let approvalUpdate =
        //   { CommentApprovalUpdateDto.CommentId = commentId
        //     Approval = json.Approval |> Option.defaultValue "" }
        //
        // let! approvalUpdateResult = ct |> postActions.UpdateCommentApproval approvalUpdate    
        // let apiResponse, statusCode =
        //   match approvalUpdateResult with
        //   | Ok _ ->
        //     { Success = true; Message = None; Data = None }, HttpStatusCode.OK
        //   | Error (Logic updateCommentApprovalError) ->
        //     let errorMessage =
        //       match updateCommentApprovalError with
        //       | UpdateCommentApprovalError.CommentNotFound commentId -> $"Comment '{commentId.ToString()}' not found for post '{slug}'"
        //     { Success = false; Message = Some errorMessage; Data = None }, HttpStatusCode.NotFound
        //   | Error actionError when actionError.IsValidation ->
        //     { Success = false;  Message = Some (actionError.ToString()); Data = None }, HttpStatusCode.BadRequest
        //   | Error actionError ->
        //     { Success = false; Message = Some (actionError.ToString()); Data = None }, HttpStatusCode.InternalServerError
          
        // response.StatusCode <- statusCode
        // do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
