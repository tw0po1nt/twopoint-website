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

open System.Net
open System.Threading

module DeleteSubscriptionAction = TwoPoint.Core.Subscriptions.Actions.DeleteSubscription

type DeleteSubscription (
  logger : ILogger<DeleteSubscription>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Admin-Subscriptions-Delete")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "internal/subscriptions/{subscriptionId}")>] req : HttpRequestData,
    context : FunctionContext,
    subscriptionId : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Subscriptions.Delete"
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
        let deleteSubscription = DeleteSubscriptionAction.withDependencies {
          new DeleteSubscriptionAction.Dependencies with

          member _.GetSubscriptionById id = subscriptionDependencies.GetSubscriptionById id
          member _.GetDeliveriesForSubscription subscription =
            subscriptionDependencies.GetDeliveriesForSubscription subscription
          member _.DeleteDelivery subscription delivery = subscriptionDependencies.DeleteDelivery subscription delivery
          member _.DeleteSubscription subscription = subscriptionDependencies.DeleteSubscription subscription
        }

        let deletion =
          { SubscriptionDeletion.Dto.SubscriptionId = SubscriptionDeletion.SubscriptionId subscriptionId }

        let! deletionResult = ct |> deleteSubscription deletion
        let apiResponse, statusCode =
          match deletionResult with
          | Ok _ ->
            { Success = true; Message = None; Data = None }, HttpStatusCode.OK
          | Error (Logic (DeleteSubscriptionAction.Error.SubscriptionNotFound _)) ->
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
