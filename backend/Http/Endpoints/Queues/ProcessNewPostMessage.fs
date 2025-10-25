namespace TwoPoint.Http.Endpoints.Queues


open Azure.Messaging.ServiceBus
open TwoPoint.Http
open TwoPoint.Core.Posts.Api
open TwoPoint.Core.Posts.Dependencies

open Azure.Data.Tables
open Azure.Communication.Email
open FirebaseAdmin.Messaging
open Microsoft.Azure.Functions.Worker
open Microsoft.Extensions.Logging

open System
open System.Threading

type ProcessNewPostMessage(
  config: Config,
  emailClient: EmailClient,
  messaging : FirebaseMessaging,
  logger : ILogger<ProcessNewPostMessage>,
  tableServiceClient: TableServiceClient
) =
  
  [<Function("Queues-ProcessNewPostMessage")>]
  member _.Run (
    [<ServiceBusTrigger("new_post", Connection = "TwoPointWebsiteServiceBus")>] message : ServiceBusReceivedMessage,
    ct : CancellationToken
  ) = task {
    let json = message.Body.FromJson<NewPostDto | null>()
    
    match json with
    | null ->
      logger.LogError("Unable to process 'new_post' message from queue. Failed to deserialize message from JSON")
    | newPostDto ->
      logger.LogInformation(
        "Processing 'new_post' message from queue: Title='{title}', Slug='{slug}'",
        newPostDto.Title,
        newPostDto.Slug
      )
      
      let validRedirectUris = config.ValidRedirectUris |> List.map _.Uri
      
      // Dependencies
      let postDependencies =
        PostDependencies.live
          validRedirectUris
          emailClient
          config.Azure.EmailSender
          messaging
          tableServiceClient
          logger
      
      let postActions = PostActions.withDependencies postDependencies
      
      let! newPostResult = (newPostDto, ct) ||> postActions.CreatePost 
      
      match newPostResult with
      | Ok (PostCreatedEvent post) ->
          logger.LogInformation("Successfully created post with slug: {slug}", post.Id.ToString())
      | Error error ->
          logger.LogError("Error creating post: {error}", error)
  }
