namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Posts
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

type DeleteCommenter(
  config: Config,
  emailClient: EmailClient,
  messaging : FirebaseMessaging,
  logger: ILogger<DeleteCommenter>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Admin-Commenters-Delete")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "internal/commenters/{validationId}")>] req : HttpRequestData,
    context : FunctionContext,
    validationId : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Commenters.Delete"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User
    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let response = req.CreateResponse HttpStatusCode.OK
        logger.LogInformation("Processing '{op}' request with validation id '{validationId}'", op, validationId)
        let validRedirectUris = config.ValidRedirectUris |> List.map _.Uri

        // Dependencies
        let postDependencies = PostDependencies(
          validRedirectUris,
          emailClient,
          config.Azure.EmailSender,
          messaging,
          tableServiceClient,
          logger
        )
        let deleteCommenter = DeleteCommenter.withDependencies {
          new DeleteCommenter.Dependencies with

          member _.GetCommenterByValidationId validationId = postDependencies.GetCommenterByValidationId validationId
          member _.GetCommentsOfCommenter commenter = postDependencies.GetCommentsOfCommenter commenter
          member _.DeleteComment comment = postDependencies.DeleteComment comment
          member _.DeleteCommenter commenter = postDependencies.DeleteCommenter commenter
        }

        let deletion = { CommenterDeletion.Dto.ValidationId = CommenterDeletion.ValidationId validationId }

        let! deletionResult = ct |> deleteCommenter deletion
        let apiResponse, statusCode =
          match deletionResult with
          | Ok _ ->
            { Success = true; Message = None; Data = None }, HttpStatusCode.OK
          | Error (Logic (DeleteCommenter.Error.CommenterNotFound validationId)) ->
            let errorMessage = $"Commenter '{validationId.ToString()}' not found"
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
