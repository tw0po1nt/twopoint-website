namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Http.Endpoints
open TwoPoint.Core.Notifications.Actions
open TwoPoint.Core.Posts.Logic
open TwoPoint.Dependencies.Notifications

open Azure.Data.Tables
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System.Net
open System.Threading

type RegisterForNotificationsJson = { Token: string }

type RegisterForNotifications(
  logger: ILogger<UpdateCommentApproval>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Admin-Notifications-Register")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "internal/notifications")>] req : HttpRequestData,
    context : FunctionContext,
    ct : CancellationToken
  ) =
    let op = "Admin.Notifications.Register"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User
    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun authInfo -> cancellableTask {
        let response = req.CreateResponse HttpStatusCode.OK
        logger.LogInformation("Processing '{op}' request", op)

        let! json = req.ReadFromJsonAsync<RegisterForNotificationsJson> ct

        // Dependencies
        let notificationDependencies = NotificationDependencies(tableServiceClient, logger)
        let registerDevice = RegisterDevice.withDependencies {
          new RegisterDevice.Dependencies with
          
          member _.GetDeviceRegistrationByUserAndClient userExternalId client =
            notificationDependencies.GetDeviceRegistrationByUserAndClient userExternalId client
          member _.CreateDeviceRegistration newDeviceRegistration =
            notificationDependencies.CreateDeviceRegistration newDeviceRegistration
          member _.UpdateDeviceRegistration registration token =
            notificationDependencies.UpdateDeviceRegistration registration token
        }

        // Inputs

        let newDeviceRegistration =
          { NewDeviceRegistration.Dto.UserExternalId = NewDeviceRegistration.UserExternalId authInfo.UserExternalId
            NewDeviceRegistration.Dto.Client = NewDeviceRegistration.Client authInfo.Client
            NewDeviceRegistration.Dto.Token = NewDeviceRegistration.Token json.Token }

        let! registrationResult = ct |> registerDevice newDeviceRegistration
        let apiResponse, statusCode =
          match registrationResult with
          | Ok _ ->
            { Success = true; Message = None; Data = None }, HttpStatusCode.OK
          | Error actionError when actionError.IsValidation ->
            { Success = false;  Message = Some (actionError.ToString()); Data = None }, HttpStatusCode.BadRequest
          | Error actionError ->
            { Success = false; Message = Some (actionError.ToString()); Data = None }, HttpStatusCode.InternalServerError

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
