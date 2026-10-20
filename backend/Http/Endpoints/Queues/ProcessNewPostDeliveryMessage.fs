namespace TwoPoint.Http.Endpoints.Queues

open TwoPoint.Core.Subscriptions
open TwoPoint.Core.Subscriptions.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Subscriptions
open TwoPoint.Http
open TwoPoint.Http.Endpoints

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

type NewPostDeliveryJson =
  { SubscriptionId : string
    Post : string
    Intro : string }

type ProcessNewPostDeliveryMessage(
  config: Config,
  credential: TokenCredential,
  emailClient: EmailClient,
  messaging : FirebaseMessaging,
  logger : ILogger<ProcessNewPostDeliveryMessage>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Queues-ProcessNewPostDeliveryMessage")>]
  member _.Run (
    [<ServiceBusTrigger(NewPostDeliveryQueue.Name, Connection = "TwoPointWebsiteServiceBus")>] message : ServiceBusReceivedMessage,
    ct : CancellationToken
  ) = task {
    let json = message.Body.FromJson<NewPostDeliveryJson | null>()

    match json, config.Welcome with
    | null, _ ->
      logger.LogError(
        "Unable to process '{queue}' message. Failed to deserialize message from JSON",
        NewPostDeliveryQueue.Name
      )
    | _, None ->
      logger.LogError(
        "Unable to process '{queue}' message. The 'Welcome' configuration section is missing",
        NewPostDeliveryQueue.Name
      )
    | deliveryJson, Some welcomeConfig ->
      logger.LogInformation(
        "Processing '{queue}' message of post '{post}' for subscription '{subscriptionId}'",
        NewPostDeliveryQueue.Name,
        deliveryJson.Post,
        deliveryJson.SubscriptionId
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
      let sendNewPostDelivery = SendNewPostDelivery.withDependencies { new SendNewPostDelivery.Dependencies with
        member _.GetSubscriptionById id = subscriptionDependencies.GetSubscriptionById id
        member _.GetDeliveriesForSubscription subscription =
          subscriptionDependencies.GetDeliveriesForSubscription subscription
        member _.GetPostBySlug slug = deliveryDependencies.GetPostBySlug slug
        member _.GetNewsletter post = deliveryDependencies.GetNewsletter post
        member _.GetUtcNow () = DateTime.UtcNow
        member _.SendNewPost subscription post intro newsletter =
          deliveryDependencies.SendNewPost subscription post intro newsletter
        member _.CreateDelivery subscription deliveryType post operationId createdDateUtc =
          subscriptionDependencies.CreateDelivery subscription deliveryType post operationId createdDateUtc
      }

      let orEmpty value = value |> Option.ofObj |> Option.defaultValue ""
      let request =
        { NewPostDeliveryRequest.Dto.SubscriptionId =
            NewPostDeliveryRequest.SubscriptionId (orEmpty deliveryJson.SubscriptionId)
          NewPostDeliveryRequest.Dto.Post = NewPostDeliveryRequest.Post (orEmpty deliveryJson.Post)
          NewPostDeliveryRequest.Dto.Intro =
            deliveryJson.Intro |> Option.ofObj |> Option.map NewPostDeliveryRequest.Intro }

      match! ct |> sendNewPostDelivery request with
      | Ok (SendNewPostDelivery.Event.Sent (subscription, delivery)) ->
        logger.LogInformation(
          "Sent new-post delivery '{deliveryId}' of post '{post}' to subscription '{subscriptionId}'",
          delivery.Id,
          delivery.Post,
          subscription.Id
        )
      | Error (ActionError.Logic error) ->
        match error with
        | SendNewPostDelivery.Error.PostNotFound _
        | SendNewPostDelivery.Error.NewsletterNotFound _
        | SendNewPostDelivery.Error.NewsletterInvalid _ ->
          logger.LogError("Unable to send new-post delivery: {error}", error)
        | _ ->
          logger.LogInformation("Skipped new-post delivery: {error}", error)
      | Error (ActionError.Validation errors) ->
        logger.LogError("Invalid '{queue}' message: {errors}", NewPostDeliveryQueue.Name, errors)
      | Error (ActionError.Dependency error) ->
        // Throw so that Service Bus delivers the message again.
        logger.LogError("Unable to send new-post delivery: {error}", error.DebugMessage)
        failwith $"New-post delivery failed in a dependency: {error.Message}"
  }
