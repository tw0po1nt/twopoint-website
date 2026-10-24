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

type UpdateCommenterStatusJson =
  { Status: string option }

type UpdateCommenterStatus(
  config: Config,
  emailClient: EmailClient,
  messaging : FirebaseMessaging,
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
        logger.LogInformation("Processing '{op}' request", op)
        let validRedirectUris = config.ValidRedirectUris |> List.map _.Uri

        let! json = req.ReadFromJsonAsync<UpdateCommenterStatusJson> ct

        // Dependencies
        let postDependencies = PostDependencies(
          validRedirectUris,
          emailClient,
          config.Azure.EmailSender,
          messaging,
          tableServiceClient,
          logger
        )
        let updateCommenterStatus = UpdateCommenterStatus.withDependencies {
          new UpdateCommenterStatus.Dependencies with

          member _.GetCommenterByValidationId validationId = postDependencies.GetCommenterByValidationId validationId
          member _.UpdateCommenterStatus commenter status = postDependencies.UpdateCommenterStatus commenter status
        }

        let statusUpdate =
          { CommenterStatusUpdate.Dto.ValidationId = CommenterStatusUpdate.ValidationId validationId
            CommenterStatusUpdate.Dto.Status =
              json.Status
              |> Option.map CommenterStatusUpdate.Status
              |> Option.defaultValue (CommenterStatusUpdate.Status "") }

        let! statusUpdateResult = ct |> updateCommenterStatus statusUpdate
        let apiResponse, statusCode =
          match statusUpdateResult with
          | Ok _ ->
            { Success = true; Message = None; Data = None }, HttpStatusCode.OK
          | Error (Logic (UpdateCommenterStatus.Error.CommenterNotFound _)) ->
            { Success = false; Message = Some "Commenter not found"; Data = None }, HttpStatusCode.NotFound
          | Error (Logic (UpdateCommenterStatus.Error.ChangeNotAllowed (current, requested))) ->
            let errorMessage = $"Commenter is {current} and cannot become {requested}"
            { Success = false; Message = Some errorMessage; Data = None }, HttpStatusCode.Conflict
          | Error actionError when actionError.IsValidation ->
            { Success = false;  Message = Some (actionError.ToString()); Data = None }, HttpStatusCode.BadRequest
          | Error actionError ->
            { Success = false; Message = Some (actionError.ToString()); Data = None }, HttpStatusCode.InternalServerError

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
