namespace TwoPoint.Http.Endpoints.Queues

open TwoPoint.Http
open TwoPoint.Http.Endpoints
open TwoPoint.Core.Subscriptions.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Subscriptions

open Azure.Communication.Email
open Azure.Core
open Azure.Data.Tables
open Azure.Messaging.ServiceBus
open Azure.Storage.Blobs
open FirebaseAdmin.Messaging
open Microsoft.Azure.Functions.Worker
open Microsoft.Extensions.Logging

open System
open System.Threading

type WelcomeJson =
  { SubscriptionId : string }

type ProcessWelcomeMessage(
  config: Config,
  credential: TokenCredential,
  emailClient: EmailClient,
  messaging : FirebaseMessaging,
  logger : ILogger<ProcessWelcomeMessage>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Queues-ProcessWelcomeMessage")>]
  member _.Run (
    [<ServiceBusTrigger(WelcomeQueue.Name, Connection = "TwoPointWebsiteServiceBus")>] message : ServiceBusReceivedMessage,
    ct : CancellationToken
  ) = task {
    let json = message.Body.FromJson<WelcomeJson | null>()

    match json, config.Welcome with
    | null, _ ->
      logger.LogError("Unable to process '{queue}' message. Failed to deserialize message from JSON", WelcomeQueue.Name)
    | _, None ->
      logger.LogError("Unable to process '{queue}' message. The 'Welcome' configuration section is missing", WelcomeQueue.Name)
    | welcomeJson, Some welcomeConfig ->
      logger.LogInformation(
        "Processing '{queue}' message for subscription '{subscriptionId}'",
        WelcomeQueue.Name,
        welcomeJson.SubscriptionId
      )

      // Dependencies
      let subscriptionDependencies = SubscriptionDependencies(tableServiceClient, logger)
      let deliveryDependencies = DeliveryDependencies(
        emailClient,
        config.Azure.EmailSender,
        messaging,
        BlobContainerClient(welcomeConfig.NewsletterContainerUri, credential),
        welcomeConfig.UnsubscribeUri,
        tableServiceClient,
        logger
      )

      // Action
      let sendWelcome = SendWelcome.withDependencies { new SendWelcome.Dependencies with
        member _.GetSubscriptionById id = subscriptionDependencies.GetSubscriptionById id
        member _.GetDeliveriesForSubscription subscription =
          subscriptionDependencies.GetDeliveriesForSubscription subscription
        member _.GetLatestPost () = deliveryDependencies.GetLatestPost()
        member _.GetNewsletter post = deliveryDependencies.GetNewsletter post
        member _.GetUtcNow () = DateTime.UtcNow
        member _.SendWelcome subscription newsletter = deliveryDependencies.SendWelcome subscription newsletter
        member _.CreateDelivery subscription deliveryType post operationId createdDateUtc =
          subscriptionDependencies.CreateDelivery subscription deliveryType post operationId createdDateUtc
      }

      let subscriptionId = welcomeJson.SubscriptionId |> Option.ofObj |> Option.defaultValue ""
      let request = { WelcomeRequest.Dto.SubscriptionId = WelcomeRequest.SubscriptionId subscriptionId }

      match! ct |> sendWelcome request with
      | Ok (SendWelcome.Event.Sent (subscription, delivery)) ->
        logger.LogInformation(
          "Sent welcome '{deliveryId}' for post '{post}' to subscription '{subscriptionId}'",
          delivery.Id,
          delivery.Post,
          subscription.Id
        )
      | Error (ActionError.Logic error) ->
        match error with
        | SendWelcome.Error.NewsletterNotFound _
        | SendWelcome.Error.NewsletterInvalid _
        | SendWelcome.Error.NoPosts ->
          logger.LogError("Unable to send welcome: {error}", error)
        | _ ->
          logger.LogInformation("Skipped welcome: {error}", error)
      | Error (ActionError.Validation errors) ->
        logger.LogError("Invalid '{queue}' message: {errors}", WelcomeQueue.Name, errors)
      | Error (ActionError.Dependency error) ->
        // Throw so that Service Bus delivers the message again.
        logger.LogError("Unable to send welcome: {error}", error.DebugMessage)
        failwith $"Welcome failed in a dependency: {error.Message}"
  }
