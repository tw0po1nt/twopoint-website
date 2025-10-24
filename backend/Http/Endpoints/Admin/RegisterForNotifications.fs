namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Notifications.Api
open TwoPoint.Core.Notifications.Dependencies
open TwoPoint.Core.Posts.Logic
open TwoPoint.Http.Endpoints

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
        
        let! json = req.ReadFromJsonAsync<RegisterForNotificationsJson>(ct)
    
        // Dependencies
        let notificationDependencies = NotificationDependencies.live tableServiceClient logger
        let notificationActions = NotificationActions.withDependencies notificationDependencies
        
        // Inputs
       
        let newDeviceRegistration =
          { NewDeviceRegistrationDto.UserExternalId = authInfo.UserExternalId
            Client = authInfo.Client
            Token = json.Token }
        
        let! registrationResult = ct |> notificationActions.RegisterDevice newDeviceRegistration    
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
