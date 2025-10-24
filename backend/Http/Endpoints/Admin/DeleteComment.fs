namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Posts.Api
open TwoPoint.Core.Posts.Dependencies
open TwoPoint.Core.Posts.Logic
open TwoPoint.Core.Util
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Communication.Email
open Azure.Data.Tables
open FirebaseAdmin.Messaging
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System.Net
open System.Threading

type DeleteComment(
  config: Config,
  emailClient: EmailClient,
  messaging : FirebaseMessaging,
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
        let postDependencies =
          PostDependencies.live
            validRedirectUris
            emailClient
            config.Azure.EmailSender
            messaging
            tableServiceClient
            logger
        let postActions = PostActions.withDependencies postDependencies
        
        let deletion = { CommentDeletionDto.CommentId = commentId }

        let! deletionResult = ct |> postActions.DeleteComment deletion    
        let apiResponse, statusCode =
          match deletionResult with
          | Ok _ ->
            { Success = true; Message = None; Data = None }, HttpStatusCode.OK
          | Error (Logic deleteCommentError) ->
            let errorMessage =
              match deleteCommentError with
              | DeleteCommentError.CommentNotFound commentId -> $"Comment '{commentId.ToString()}' not found for post '{slug}'"
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
