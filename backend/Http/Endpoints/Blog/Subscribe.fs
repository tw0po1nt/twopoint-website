namespace TwoPoint.Http.Endpoints.Blog

open TwoPoint.Core.Subscriptions
open TwoPoint.Core.Subscriptions.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Subscriptions
open TwoPoint.Dependencies.Util
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Communication.Email
open Azure.Data.Tables
open Azure.Messaging.ServiceBus
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System
open System.Net
open System.Text.Json
open System.Threading

type NewSubscriptionJson =
  { EmailAddress : string option
    Name : string option }

/// `State` is `confirmed` or `unknown`.
type SubscriptionConfirmJson =
  { State : string }

module SubscribeMapping =

  /// A refusal of the logic gets the same answer as a new subscription, so the form does not tell a stranger
  /// which addresses subscribe.
  let answer (result : ActionResult<CreateSubscription.Event, CreateSubscription.Error>) : ApiResponse<unit> * HttpStatusCode =
    match result with
    | Ok _
    | Error (Logic _) -> { Success = true; Message = None; Data = None }, HttpStatusCode.OK
    | Error actionError when actionError.IsValidation ->
      { Success = false; Message = Some (actionError.ToString()); Data = None }, HttpStatusCode.BadRequest
    | Error actionError -> { Success = false; Message = Some (actionError.ToString()); Data = None }, HttpStatusCode.InternalServerError

  let missingConfig : ApiResponse<unit> * HttpStatusCode =
    { Success = false; Message = Some "The 'Subscription' configuration section is missing"; Data = None },
    HttpStatusCode.InternalServerError

module SubscriptionConfirmMapping =

  let toJson (state : SubscriptionConfirmState) : SubscriptionConfirmJson =
    match state with
    | SubscriptionConfirmState.Confirmed -> { State = "confirmed" }
    | SubscriptionConfirmState.Unknown -> { State = "unknown" }

  let answer (result : Result<ConfirmSubscription.Event, DependencyError>) : ApiResponse<SubscriptionConfirmJson> * HttpStatusCode =
    match result with
    | Ok event -> { Success = true; Message = None; Data = Some (toJson (ConfirmSubscription.Event.state event)) }, HttpStatusCode.OK
    | Error error -> { Success = false; Message = Some error.Message; Data = None }, HttpStatusCode.InternalServerError

module internal WelcomeQueueing =

  let enqueue (serviceBus : ServiceBusClient) (logger : ILogger) (subscription : Subscription) ct = task {
    try
      use sender = serviceBus.CreateSender WelcomeQueue.Name
      let json = JsonSerializer.Serialize {| subscriptionId = subscription.Id.ToString() |}
      do! sender.SendMessageAsync(ServiceBusMessage json, ct)
      logger.LogInformation("Queued a welcome for subscription '{subscriptionId}'", subscription.Id)
    with ex ->
      logger.LogError(ex, "Unable to queue a welcome for subscription '{subscriptionId}'", subscription.Id)
  }

type Subscribe (
  config: Config,
  emailClient: EmailClient,
  logger: ILogger<Subscribe>,
  tableServiceClient: TableServiceClient
) =

  let write (req : HttpRequestData) (ct : CancellationToken) (apiResponse : ApiResponse<unit>, statusCode : HttpStatusCode) = task {
    let response = req.CreateResponse statusCode
    do! response.WriteAsJsonAsync(apiResponse, ct)
    return response
  }

  [<Function("Blog-Subscribe")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "blog/subscriptions")>] req : HttpRequestData,
    ct : CancellationToken
  ) = task {
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

    match config.Subscription with
    | None ->
      logger.LogError "Unable to subscribe. The 'Subscription' configuration section is missing"
      return! write req ct SubscribeMapping.missingConfig
    | Some subscriptionConfig ->
      // Dependencies
      let subscriptionDependencies = SubscriptionDependencies(tableServiceClient, SystemDependencies.live, logger)
      let confirmationDependencies =
        ConfirmationDependencies(
          emailClient,
          config.Azure.EmailSender,
          subscriptionConfig.ConfirmUri,
          tableServiceClient,
          SystemDependencies.live,
          logger
        )

      // Inputs
      let newSubscription =
        { NewSubscription.Dto.EmailAddress = json.EmailAddress |> Option.defaultValue "" |> NewSubscription.EmailAddress
          NewSubscription.Dto.Name = json.Name |> Option.map NewSubscription.Name }

      // Action
      let createSubscription = CreateSubscription.withDependencies { new CreateSubscription.Dependencies with
        member _.GetSubscriptionByEmail email = subscriptionDependencies.GetSubscriptionByEmail email
        member _.GetUtcNow () = DateTime.UtcNow
        member _.NewSubscriptionId () = subscriptionDependencies.NewSubscriptionId()
        member _.SendConfirmation subscription = confirmationDependencies.SendConfirmation subscription
        member _.CreateSubscription subscription = subscriptionDependencies.CreateSubscription subscription
        member _.UpdateSubscriptionStatus subscription = subscriptionDependencies.UpdateSubscriptionStatus subscription
      }

      let! result = ct |> createSubscription newSubscription

      match result with
      | Ok (CreateSubscription.Event.Created subscription)
      | Ok (CreateSubscription.Event.Requested subscription) ->
        logger.LogInformation("Sent a confirmation for subscription '{subscriptionId}'", subscription.Id)
      | Error (Logic error) -> logger.LogInformation("Sent no confirmation: {error}", error)
      | Error actionError -> logger.LogError("Unable to subscribe: {error}", actionError)

      return! write req ct (SubscribeMapping.answer result)
  }

type ConfirmSubscriptionEndpoint (
  config: Config,
  logger: ILogger<ConfirmSubscriptionEndpoint>,
  serviceBus: ServiceBusClient,
  tableServiceClient: TableServiceClient
) =

  [<Function("Blog-Subscriptions-Confirm")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "blog/subscriptions/{subscriptionId}/confirm")>] req : HttpRequestData,
    subscriptionId : string,
    ct : CancellationToken
  ) = task {
    logger.LogInformation("Processing 'Blog.Subscriptions.Confirm' request for subscription '{subscriptionId}'", subscriptionId)

    let subscriptionDependencies = SubscriptionDependencies(tableServiceClient, SystemDependencies.live, logger)

    let confirm = ConfirmSubscription.withDependencies { new ConfirmSubscription.Dependencies with
      member _.GetSubscriptionById id = subscriptionDependencies.GetSubscriptionById id
      member _.UpdateSubscriptionStatus subscription = subscriptionDependencies.UpdateSubscriptionStatus subscription
    }

    let! result = ct |> confirm subscriptionId

    match result with
    | Ok (ConfirmSubscription.Event.Activated subscription) when config.Welcome |> Option.exists _.Enabled ->
      do! WelcomeQueueing.enqueue serviceBus logger subscription ct
    | Ok _ -> ()
    | Error error -> logger.LogError("Unable to confirm subscription: {error}", error.DebugMessage)

    let apiResponse, statusCode = SubscriptionConfirmMapping.answer result
    let response = req.CreateResponse statusCode
    do! response.WriteAsJsonAsync(apiResponse, ct)
    return response
  }
