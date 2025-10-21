namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Posts.Api
open TwoPoint.Core.Posts.Dependencies
open TwoPoint.Core.Posts.Logic
open TwoPoint.Core.Util
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

type UpdateCommentApprovalJson =
  { Approval: string option }

type UpdateCommentApproval(
  config: Config,
  emailClient: EmailClient,
  logger: ILogger<UpdateCommentApproval>,
  tableServiceClient: TableServiceClient
) =
  
  [<Function("Admin-Posts-Comments-UpdateApproval")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "internal/posts/{slug}/comments/{commentId}")>] req : HttpRequestData,
    slug : string,
    commentId : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.Comments.Approve"
    ct |> (
      Auth.runIfAuthorized config logger req op
      <| cancellableTask {
        let response = req.CreateResponse HttpStatusCode.OK
        logger.LogInformation("Processing 'Admin.Posts.Comments.Approve' request with slug '{slug}' and comment id '{commentId}'", slug, commentId)
        let validRedirectUris = config.ValidRedirectUris |> List.map _.Uri
        
        let! json = req.ReadFromJsonAsync<UpdateCommentApprovalJson>(ct)
        
        // Dependencies
        let postDependencies = PostDependencies.live validRedirectUris emailClient config.Azure.EmailSender tableServiceClient logger
        let postActions = PostActions.withDependencies postDependencies
        
        let approvalUpdate =
          { CommentApprovalUpdateDto.CommentId = commentId
            Approval = json.Approval |> Option.defaultValue "" }

        let! approvalUpdateResult = ct |> postActions.UpdateCommentApproval approvalUpdate    
        let apiResponse, statusCode =
          match approvalUpdateResult with
          | Ok _ ->
            { Success = true; Message = None; Data = None }, HttpStatusCode.OK
          | Error (Logic updateCommentApprovalError) ->
            let errorMessage =
              match updateCommentApprovalError with
              | UpdateCommentApprovalError.CommentNotFound commentId -> $"Comment '{commentId.ToString()}' not found for post '{slug}'"
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
