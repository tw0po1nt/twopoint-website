namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Subscriptions.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Subscriptions
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Data.Tables
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System
open System.Net
open System.Threading

module CancelSubscriptionAction = TwoPoint.Core.Subscriptions.Actions.CancelSubscription

type CancelSubscription (
  logger : ILogger<CancelSubscription>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Admin-Subscriptions-Cancel")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "internal/subscriptions/{subscriptionId}/cancellations")>] req : HttpRequestData,
    context : FunctionContext,
    subscriptionId : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Subscriptions.Cancel"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request with subscription id '{subscriptionId}'", op, subscriptionId)
        let response = req.CreateResponse HttpStatusCode.OK

        // Dependencies
        let subscriptionDependencies = SubscriptionDependencies(tableServiceClient, logger)
        let cancelSubscription = CancelSubscriptionAction.withDependencies {
          new CancelSubscriptionAction.Dependencies with

          member _.GetSubscriptionByEmail email = subscriptionDependencies.GetSubscriptionByEmail email
          member _.GetSubscriptionById id = subscriptionDependencies.GetSubscriptionById id
          member _.GetUtcNow () = DateTime.UtcNow
          member _.DeactivateSubscription subscription = subscriptionDependencies.UpdateSubscriptionActivation subscription
        }

        let cancellation =
          { SubscriptionCancellation.Dto.EmailAddress = None
            SubscriptionCancellation.Dto.SubscriptionId = Some (SubscriptionCancellation.SubscriptionId subscriptionId) }

        let! cancelResult = ct |> cancelSubscription cancellation
        let apiResponse, statusCode =
          match cancelResult with
          | Ok canceled ->
            let (CancelSubscriptionAction.Event.Cancelled subscription) = canceled
            { Success = true; Message = None; Data = Some (subscription.ToDto()) }, HttpStatusCode.OK
          | Error (Logic (CancelSubscriptionAction.Error.SubscriptionNotFound _)) ->
            { Success = false; Message = Some $"Subscription '{subscriptionId}' not found"; Data = None }, HttpStatusCode.NotFound
          | Error actionError when actionError.IsValidation ->
            { Success = false; Message = Some (actionError.ToString()); Data = None }, HttpStatusCode.BadRequest
          | Error actionError ->
            { Success = false; Message = Some (actionError.ToString()); Data = None }, HttpStatusCode.InternalServerError

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
