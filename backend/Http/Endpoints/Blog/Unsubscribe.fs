namespace TwoPoint.Http.Endpoints.Blog

open TwoPoint.Core.Subscriptions.Api
open TwoPoint.Core.Subscriptions.Dependencies
open TwoPoint.Core.Subscriptions.Logic
open TwoPoint.Core.Util
open TwoPoint.Http.Endpoints

open Azure.Data.Tables
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System
open System.Net
open System.Threading

type SubscriptionCancellationJson =
  { EmailAddress : string option
    SubscriptionId : string option }

type Unsubscribe (
  logger: ILogger<Unsubscribe>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Blog-Unsubscribe")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "blog/subscriptions/cancellations")>] req : HttpRequestData,
    ct : CancellationToken
  ) = task {
    let response = req.CreateResponse HttpStatusCode.OK
    let! json = req.ReadFromJsonAsync<SubscriptionCancellationJson> ct
    let email =
      json.EmailAddress
      |> Option.map (fun em -> if em = "" then "<not provided>" else $"{em[0]}*****@*****.***")
      |> Option.defaultValue "<not provided>"
    let subscriptionId =
      json.SubscriptionId
      |> Option.map (fun sid -> if sid = "" then "<not provided>" else sid)
      |> Option.defaultValue "<not provided>"


    logger.LogInformation("Processing 'Blog.Unsubscribe' request with email '{email}', subscriptionId '{subscriptionId}", email, subscriptionId)

    // Dependencies
    let subscriptionDependencies =
      SubscriptionDependencies.live
        tableServiceClient
        logger

    // Inputs
    let subscriptionCancellation =
      { SubscriptionCancellation.Dto.EmailAddress = json.EmailAddress |> Option.map SubscriptionCancellation.EmailAddress
        SubscriptionCancellation.Dto.SubscriptionId = json.SubscriptionId |> Option.map SubscriptionCancellation.SubscriptionId }

    // Action
    let cancelSubscription = CancelSubscription.withDependencies { new CancelSubscription.Dependencies with
      member _.GetSubscriptionByEmail email =
        subscriptionDependencies.GetSubscriptionByEmail email
      member _.GetSubscriptionById id =
        subscriptionDependencies.GetSubscriptionById id 
      member _.DeactivateSubscription subscription = 
        subscriptionDependencies.UpdateSubscriptionActivation subscription false 
    }

    let! cancelSubscriptionResult = ct |> cancelSubscription subscriptionCancellation 
    let apiResponse, statusCode =
      match cancelSubscriptionResult with
      | Ok _ | Error (Logic _) ->
        { Success = true; Message = None; Data = None }, HttpStatusCode.OK
      | Error actionError when actionError.IsValidation ->
        { Success = false;  Message = Some (actionError.ToString()); Data = None }, HttpStatusCode.BadRequest
      | Error actionError ->
        { Success = false; Message = Some (actionError.ToString()); Data = None }, HttpStatusCode.InternalServerError

    response.StatusCode <- statusCode
    do! response.WriteAsJsonAsync(apiResponse, ct)
    return response
  }
