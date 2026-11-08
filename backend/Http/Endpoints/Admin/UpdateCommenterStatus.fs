namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Posts
open TwoPoint.Dependencies.Util
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

type UpdateCommenterStatusJson =
  { Status: string option }

type UpdateCommenterStatus(
  config: Config,
  emailClient: EmailClient,
  logger: ILogger<UpdateCommenterStatus>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Admin-Commenters-UpdateStatus")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "internal/commenters/{validationId}")>] req : HttpRequestData,
    context : FunctionContext,
    validationId : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Commenters.UpdateStatus"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User
    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let response = req.CreateResponse HttpStatusCode.OK
        logger.LogInformation("Processing '{op}' request with validation id '{validationId}'", op, validationId)
        let validRedirectUris = config.ValidRedirectUris |> List.map _.Uri

        let! json = req.ReadFromJsonAsync<UpdateCommenterStatusJson> ct

        // Dependencies
        let postDependencies = PostDependencies(
          validRedirectUris,
          emailClient,
          config.Azure.EmailSender,
          tableServiceClient,
          SystemDependencies.live,
          logger
        )
        let updateCommenterStatus = UpdateCommenterStatus.withDependencies {
          new UpdateCommenterStatus.Dependencies with

          member _.GetCommenterByValidationId validationId = postDependencies.GetCommenterByValidationId validationId
          member _.GetCommentsOfCommenter commenter = postDependencies.GetCommentsOfCommenter commenter
          member _.UpdateCommenterStatus commenter status = postDependencies.UpdateCommenterStatus commenter status
          member _.UpdateCommentApproval comment approval = postDependencies.UpdateCommentApproval comment approval
        }

        let statusUpdate =
          { CommenterStatusUpdate.Dto.ValidationId = CommenterStatusUpdate.ValidationId validationId
            CommenterStatusUpdate.Dto.Status =
              json.Status
              |> Option.map CommenterStatusUpdate.Status
              |> Option.defaultValue (CommenterStatusUpdate.Status "") }

        let! statusUpdateResult = ct |> updateCommenterStatus statusUpdate
        let apiResponse, statusCode =
          statusUpdateResult
          |> ActionResult.toApiResponse
            (fun _ -> None)
            (function
              | UpdateCommenterStatus.Error.CommenterNotFound validationId ->
                $"Commenter '{validationId.ToString()}' not found", HttpStatusCode.NotFound
              | UpdateCommenterStatus.Error.ChangeNotAllowed(current, requested) ->
                $"Commenter is {current} and cannot become {requested}", HttpStatusCode.Conflict)

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
