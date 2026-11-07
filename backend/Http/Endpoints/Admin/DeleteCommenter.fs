namespace TwoPoint.Http.Endpoints.Admin

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

type DeleteCommenter(
  config: Config,
  emailClient: EmailClient,
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
          tableServiceClient,
          logger
        )
        let deleteCommenter = DeleteCommenter.withDependencies {
          new DeleteCommenter.Dependencies with

          member _.GetCommenterByValidationId validationId = postDependencies.GetCommenterByValidationId validationId
          member _.GetCommentsOfCommenter commenter = postDependencies.GetCommentsOfCommenter commenter
          member _.GetRepliesOf topLevels = postDependencies.GetRepliesOf topLevels
          member _.DeleteComment comment = postDependencies.DeleteComment comment
          member _.DeleteCommenter commenter = postDependencies.DeleteCommenter commenter
        }

        let deletion = { CommenterDeletion.Dto.ValidationId = CommenterDeletion.ValidationId validationId }

        let! deletionResult = ct |> deleteCommenter deletion

        match deletionResult with
        | Ok (DeleteCommenter.Event.Deleted (_, others)) ->
          logger.LogInformation("Deleted commenter '{validationId}' with {others} replies from other writers", validationId, others)
        | Error _ -> ()
        let apiResponse, statusCode =
          deletionResult
          |> ActionResult.toApiResponse
            (fun _ -> None)
            (fun (DeleteCommenter.Error.CommenterNotFound validationId) ->
              $"Commenter '{validationId.ToString()}' not found", HttpStatusCode.NotFound)

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
