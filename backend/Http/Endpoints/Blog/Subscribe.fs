namespace TwoPoint.Http.Endpoints.Blog

open TwoPoint.Http.Endpoints
open TwoPoint.Core.Subscriptions.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Subscriptions

open Azure.Data.Tables
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System
open System.Net
open System.Threading

type NewSubscriptionJson =
  { EmailAddress : string option
    Name : string option }

type Subscribe (
  logger: ILogger<Subscribe>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Blog-Subscribe")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "blog/subscriptions")>] req : HttpRequestData,
    ct : CancellationToken
  ) = task {
    let response = req.CreateResponse HttpStatusCode.OK
    let! json = req.ReadFromJsonAsync<NewSubscriptionJson> ct
    let email =
      json.EmailAddress
      |> Option.map (fun em -> if em = "" then "<not provided>" else $"{em[0]}*****@*****.***")
      |> Option.defaultValue "<not provided>"
    let name =
      json.Name
      |> Option.map (konst "<redacted>")
      |> Option.defaultValue "<not provided>"

    logger.LogInformation("Processing 'Blog.Subscribe' request with email '{email}', name '{name}'", email, name)

    // Dependencies
    let subscriptionDependencies = SubscriptionDependencies(
      tableServiceClient,
      logger
    )

    // Inputs
    let newSubscription =
      { NewSubscription.Dto.EmailAddress = json.EmailAddress |> Option.defaultValue "" |> NewSubscription.EmailAddress
        NewSubscription.Dto.Name = json.Name |> Option.map NewSubscription.Name }

    // Action
    let createSubscription = CreateSubscription.withDependencies { new CreateSubscription.Dependencies with
      member _.GetSubscriptionByEmail email =
        subscriptionDependencies.GetSubscriptionByEmail email
      member _.GetUtcNow () = 
        DateTime.UtcNow
      member _.CreateSubscription subscription createdDateUtc = 
        subscriptionDependencies.CreateSubscription subscription createdDateUtc
      member _.ActivateSubscription subscription = 
        subscriptionDependencies.UpdateSubscriptionActivation subscription true
    }

    let! createSubscriptionResult = ct |> createSubscription newSubscription
    let apiResponse, statusCode =
      match createSubscriptionResult with
      | Ok _ | Error (Logic ()) ->
        { Success = true; Message = None; Data = None }, HttpStatusCode.OK
      | Error actionError when actionError.IsValidation ->
        { Success = false;  Message = Some (actionError.ToString()); Data = None }, HttpStatusCode.BadRequest
      | Error actionError ->
        { Success = false; Message = Some (actionError.ToString()); Data = None }, HttpStatusCode.InternalServerError

    response.StatusCode <- statusCode
    do! response.WriteAsJsonAsync(apiResponse, ct)
    return response
  }
