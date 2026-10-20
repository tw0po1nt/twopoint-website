namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Subscriptions
open TwoPoint.Core.Subscriptions.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Subscriptions
open TwoPoint.Dependencies.Util
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Communication.Email
open Azure.Core
open Azure.Data.Tables
open Azure.Messaging.ServiceBus
open Azure.Storage.Blobs
open FirebaseAdmin.Messaging
open FsToolkit.ErrorHandling
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System
open System.Net
open System.Text.Json
open System.Threading

type StartNewPostDeliveryJson =
  { Intro : string option }

type StartNewPostDeliveryResponse =
  { Messages : int }

type StartNewPostDelivery(
  config: Config,
  credential: TokenCredential,
  emailClient: EmailClient,
  messaging : FirebaseMessaging,
  serviceBus : ServiceBusClient,
  logger : ILogger<StartNewPostDelivery>,
  tableServiceClient: TableServiceClient
) =

  let queueDeliveries (post : Post) (intro : NonEmptyString option) (recipients : Subscription list) =
    runDependencyWithLogging Dependency.ServiceBus logger $"{nameof StartNewPostDelivery}.QueueDeliveries"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      use sender = serviceBus.CreateSender NewPostDeliveryQueue.Name

      let mutable pending =
        recipients
        |> List.map (fun subscription ->
          JsonSerializer.Serialize
            {| subscriptionId = subscription.Id.ToString()
               post = Slug.value post.Id
               intro = intro |> Option.map NonEmptyString.value |> Option.toObj |}
          |> ServiceBusMessage
        )

      while not pending.IsEmpty do
        use! batch = sender.CreateMessageBatchAsync ct
        let rest = pending |> List.skipWhile batch.TryAddMessage
        if rest.Length = pending.Length then
          failwith $"A '{NewPostDeliveryQueue.Name}' message is too large for a batch"
        do! sender.SendMessagesAsync(batch, ct)
        pending <- rest
    }

  [<Function("Admin-Posts-Deliveries-Start")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "internal/posts/{slug}/deliveries")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.Deliveries.Start"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        logger.LogInformation("Processing '{op}' request for post '{slug}'", op, slug)
        let response = req.CreateResponse HttpStatusCode.OK

        let! body = req.ReadAsStringAsync()
        let json =
          body
          |> Option.ofObj
          |> Option.filter (String.IsNullOrWhiteSpace >> not)
          |> Option.map (fun body ->
            JsonSerializer.Deserialize<StartNewPostDeliveryJson>(body, JsonSerializerOptions JsonSerializerDefaults.Web)
          )

        let! apiResponse, statusCode =
          match config.Welcome with
          | None ->
            logger.LogError("Unable to start new-post delivery. The 'Welcome' configuration section is missing")
            let apiResponse : ApiResponse<StartNewPostDeliveryResponse> =
              { Success = false; Message = Some "The 'Welcome' configuration section is missing"; Data = None }
            CancellableTask.singleton (apiResponse, HttpStatusCode.InternalServerError)
          | Some welcomeConfig -> cancellableTask {
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
            let startNewPostDelivery = StartNewPostDelivery.withDependencies { new StartNewPostDelivery.Dependencies with
              member _.GetPostBySlug slug = deliveryDependencies.GetPostBySlug slug
              member _.GetNewsletter post = deliveryDependencies.GetNewsletter post
              member _.GetAllSubscriptions () = subscriptionDependencies.GetAllSubscriptions()
              member _.GetUtcNow () = DateTime.UtcNow
              member _.QueueDeliveries post intro recipients = queueDeliveries post intro recipients
            }

            let start =
              { NewPostDeliveryStart.Dto.Post = NewPostDeliveryStart.Post slug
                NewPostDeliveryStart.Dto.Intro =
                  json |> Option.bind _.Intro |> Option.map NewPostDeliveryStart.Intro }

            match! ct |> startNewPostDelivery start with
            | Ok (StartNewPostDelivery.Event.Started (post, recipients)) ->
              logger.LogInformation(
                "Queued {count} new-post deliveries of post '{post}'",
                recipients.Length,
                post.Id
              )
              let apiResponse =
                { Success = true; Message = None; Data = Some { Messages = recipients.Length } }
              return apiResponse, HttpStatusCode.OK
            | Error (ActionError.Logic error) ->
              let message, statusCode =
                match error with
                | StartNewPostDelivery.Error.PostNotFound slug ->
                  $"Post '{slug}' not found", HttpStatusCode.NotFound
                | StartNewPostDelivery.Error.PostUpcoming post ->
                  $"Post '{post.Id}' is not published until {post.Info.CreatedDate:o}", HttpStatusCode.Conflict
                | StartNewPostDelivery.Error.NewsletterNotFound post ->
                  $"The newsletter of post '{post.Id}' does not exist", HttpStatusCode.Conflict
                | StartNewPostDelivery.Error.NewsletterInvalid (post, reason) ->
                  $"The newsletter of post '{post.Id}' is invalid: {reason}", HttpStatusCode.Conflict
              logger.LogWarning("Refused new-post delivery: {error}", message)
              return { Success = false; Message = Some message; Data = None }, statusCode
            | Error actionError when actionError.IsValidation ->
              return { Success = false; Message = Some (actionError.ToString()); Data = None }, HttpStatusCode.BadRequest
            | Error actionError ->
              return
                { Success = false; Message = Some (actionError.ToString()); Data = None },
                HttpStatusCode.InternalServerError
          }

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
