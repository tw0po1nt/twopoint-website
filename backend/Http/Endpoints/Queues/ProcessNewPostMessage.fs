namespace TwoPoint.Http.Endpoints.Queues

open TwoPoint.Http
open TwoPoint.Core.Posts.Actions
open TwoPoint.Dependencies.Posts

open Azure.Communication.Email
open Azure.Data.Tables
open Azure.Messaging.ServiceBus
open FirebaseAdmin.Messaging
open Microsoft.Azure.Functions.Worker
open Microsoft.Extensions.Logging

open System
open System.Threading

type NewPostJson =
  { Title : string 
    Slug : string 
    CreatedDate : string }

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
    let json = message.Body.FromJson<NewPostJson | null>()

    match json with
    | null ->
      logger.LogError "Unable to process 'new_post' message from queue. Failed to deserialize message from JSON"
    | newPostJson ->
      logger.LogInformation(
        "Processing 'new_post' message from queue: Title='{title}', Slug='{slug}'",
        newPostJson.Title,
        newPostJson.Slug
      )

      let validRedirectUris = config.ValidRedirectUris |> List.map _.Uri

      // Dependencies
      let postDependencies = PostDependencies(
        validRedirectUris,
        emailClient,
        config.Azure.EmailSender,
        messaging,
        tableServiceClient,
        logger
      )

      let createPost = CreatePost.withDependencies {
        new CreatePost.Dependencies with
        
        member _.GetPostBySlug slug = postDependencies.GetPostBySlug slug
        member _.CreatePost newPost = postDependencies.CreatePost newPost
      }
      
      let newPost =
        { NewPost.Dto.Title = NewPost.Title newPostJson.Title
          NewPost.Dto.Slug = NewPost.Slug newPostJson.Slug
          NewPost.Dto.CreatedDate = NewPost.CreatedDate newPostJson.CreatedDate }

      let! newPostResult = (newPost, ct) ||> createPost

      match newPostResult with
      | Ok (PostCreatedEvent post) ->
          logger.LogInformation("Successfully created post with slug: {slug}", post.Id.ToString())
      | Error error ->
          logger.LogError("Error creating post: {error}", error)
  }
