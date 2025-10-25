namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Http
open TwoPoint.Http.Endpoints
open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Api
open TwoPoint.Core.Posts.Dependencies

open Azure.Communication.Email
open Azure.Data.Tables
open Azure.Messaging.ServiceBus
open FirebaseAdmin.Messaging
open FSharp.Data
open FsToolkit.ErrorHandling
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System
open System.Globalization
open System.Net
open System.Text.Json
open System.Threading

type TwoPointRss = XmlProvider<"https://twopoint.dev/rss.xml">

type SyncPosts (
  config: Config,
  emailClient: EmailClient,
  messaging : FirebaseMessaging,
  serviceBus : ServiceBusClient,
  logger : ILogger<GetAllPosts>,
  tableServiceClient: TableServiceClient
) =
  
  [<Function("Admin-Posts-Sync")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "internal/posts")>] req : HttpRequestData,
    context : FunctionContext,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.Sync"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User
    
    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        logger.LogInformation("Processing '{op}' request", op)
        let response = req.CreateResponse HttpStatusCode.OK
        let validRedirectUris = config.ValidRedirectUris |> List.map _.Uri
        
        let! feed = TwoPointRss.AsyncGetSample()
        
        let feedPosts = 
          feed.Channel.Items
          |> Array.map (fun item -> 
            let uri = Uri(item.Link)
            let slug = uri.AbsolutePath.TrimEnd('/').Split('/') |> Array.last
            let createdDate = item.PubDate.UtcDateTime.ToString("o", CultureInfo.InvariantCulture)
            {| Title = item.Title; Slug = slug; CreatedDate = createdDate |}
          )
          |> Array.toList
        
        logger.LogInformation("Found {count} posts in RSS feed", feedPosts.Length)
        
        let postDependencies =
          PostDependencies.live
            validRedirectUris
            emailClient
            config.Azure.EmailSender
            messaging
            tableServiceClient
            logger
        let postQueries = PostQueries.withDependencies postDependencies
        
        let! newPosts = cancellableTask {
          let! existingSlugs =
            postQueries.GetAllPosts()
            |> CancellableTask.map (Result.defaultValue [])
            |> CancellableTask.map (List.map (_.Slug >> Slug.value) >> Set.ofList)
          return feedPosts |> List.filter (fun post -> not (existingSlugs.Contains post.Slug))
        }
        
        logger.LogInformation("Found {newCount} new posts out of {totalCount} feed posts", newPosts.Length, feedPosts.Length)
        
        if newPosts.Length > 0
        then do! cancellableTask {
          use sender = serviceBus.CreateSender("new_post")
          try
            let messages = newPosts |> List.map (fun post ->
              let json = JsonSerializer.Serialize({|
                title = post.Title
                slug = post.Slug
                createdDate = post.CreatedDate
              |})
              ServiceBusMessage(json)
            )
            
            let batch = messages |> List.toArray
            do! sender.SendMessagesAsync(batch, ct)
            logger.LogInformation("Sent {count} messages to 'new_post' queue", batch.Length)
          with
            | ex ->
              logger.LogError(message = "{op}: An error occurred when deleting an entity", ``exception`` = ex, args = [| op |])
        }
        else logger.LogInformation("No new posts to sync")
        
        let apiResponse : ApiResponse<unit> =
          { Success = true
            Message = Some (if newPosts.Length > 0 then "Sync started" else "Up to date")
            Data = None }  
        
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
