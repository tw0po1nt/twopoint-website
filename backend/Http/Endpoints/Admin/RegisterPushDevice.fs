namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Notifications.Actions
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

type RegisterPushDeviceJson =
  { Endpoint : string
    P256dh : string
    Auth : string }

type RegisterPushDevice(
  logger: ILogger<RegisterPushDevice>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Admin-Notifications-Devices-Register")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "internal/notifications/devices")>] req : HttpRequestData,
    context : FunctionContext,
    ct : CancellationToken
  ) =
    let op = "Admin.Notifications.Devices.Register"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User
    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun authInfo -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request", op)
        let response = req.CreateResponse HttpStatusCode.OK

        let! json = req.ReadFromJsonAsync<RegisterPushDeviceJson> ct

        let userAgent =
          match req.Headers.TryGetValues "User-Agent" with
          | true, values -> values |> Seq.tryHead |> Option.defaultValue ""
          | _ -> ""

        // Dependencies
        let deviceDependencies = DeviceDependencies(tableServiceClient, logger)
        let registerDevice = RegisterPushDevice.withDependencies {
          new RegisterPushDevice.Dependencies with

          member _.GetDevice userId deviceId = deviceDependencies.GetDevice userId deviceId
          member _.GetUtcNow () = deviceDependencies.GetUtcNow()
          member _.SaveDevice device = deviceDependencies.SaveDevice device
        }

        let dto : RegisterPushDevice.Dto =
          match box json with
          | null ->
            { UserExternalId = authInfo.UserExternalId; Endpoint = ""; P256dh = ""; Auth = ""; UserAgent = userAgent }
          | _ ->
            { UserExternalId = authInfo.UserExternalId
              Endpoint = json.Endpoint
              P256dh = json.P256dh
              Auth = json.Auth
              UserAgent = userAgent }

        let! result = ct |> registerDevice dto
        let apiResponse, statusCode =
          match result with
          | Ok (RegisterPushDevice.Event.Registered device) ->
            { Success = true; Message = None; Data = Some (device.ToDto()) }, HttpStatusCode.OK
          | Error actionError when actionError.IsValidation ->
            { Success = false; Message = Some (actionError.ToString()); Data = None }, HttpStatusCode.BadRequest
          | Error actionError ->
            { Success = false; Message = Some (actionError.ToString()); Data = None }, HttpStatusCode.InternalServerError

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
