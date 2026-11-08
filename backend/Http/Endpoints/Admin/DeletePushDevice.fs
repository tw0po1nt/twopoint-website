namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Notifications.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Notifications
open TwoPoint.Dependencies.Util
open TwoPoint.Http.Endpoints

open Azure.Data.Tables
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System.Net
open System.Threading

type DeletePushDevice(
  logger: ILogger<DeletePushDevice>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Admin-Notifications-Devices-Delete")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "internal/notifications/devices/{deviceId}")>] req : HttpRequestData,
    context : FunctionContext,
    deviceId : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Notifications.Devices.Delete"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User
    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun authInfo -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request with device id '{deviceId}'", op, deviceId)
        let response = req.CreateResponse HttpStatusCode.OK

        // Dependencies
        let deviceDependencies = DeviceDependencies(tableServiceClient, SystemDependencies.live, logger)
        let removeDevice = RemovePushDevice.withDependencies {
          new RemovePushDevice.Dependencies with

          member _.GetDevice userId deviceId = deviceDependencies.GetDevice userId deviceId
          member _.DeleteDevice device = deviceDependencies.DeleteDevice device
        }

        let! result = ct |> removeDevice { UserExternalId = authInfo.UserExternalId; DeviceId = deviceId }
        let apiResponse, statusCode =
          result
          |> ActionResult.toApiResponse
            (fun _ -> None)
            (fun (RemovePushDevice.Error.DeviceNotFound _) -> $"Device '{deviceId}' not found", HttpStatusCode.NotFound)

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
