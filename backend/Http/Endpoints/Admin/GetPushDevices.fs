namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Notifications.Queries
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Notifications
open TwoPoint.Http.Endpoints

open Azure.Data.Tables
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System.Net
open System.Threading

type GetPushDevices(
  logger: ILogger<GetPushDevices>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Admin-Notifications-Devices-GetAll")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "internal/notifications/devices")>] req : HttpRequestData,
    context : FunctionContext,
    ct : CancellationToken
  ) =
    let op = "Admin.Notifications.Devices.GetAll"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User
    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun authInfo -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request", op)
        let response = req.CreateResponse HttpStatusCode.OK

        // Dependencies
        let deviceDependencies = DeviceDependencies(tableServiceClient, logger)
        let listDevices = ListPushDevices.withDependencies {
          new ListPushDevices.Dependencies with

          member _.GetDevicesOfUser userId = deviceDependencies.GetDevicesOfUser userId
        }

        let! devicesResult = listDevices authInfo.UserExternalId
        let apiResponse, statusCode = devicesResult |> QueryResult.toApiResponse (List.map _.ToDto())

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
