namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Http
open TwoPoint.Http.Endpoints
open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Api
open TwoPoint.Core.Posts.Dependencies
open TwoPoint.Core.Util

open Azure.Communication.Email
open Azure.Data.Tables
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System
open System.Net
open System.Threading

type PostCommentStatsDto =
  { New : uint
    Approved : uint
    Rejected : uint }
  
type PostDto =
  { Slug : string
    Title : string
    CreatedDate : DateTime
    CommentStats : PostCommentStatsDto }
  
[<AutoOpen>]
module PostTypesExt =
  
  type PostCommentStats with
    member this.ToDto() =
      { PostCommentStatsDto.New = this.New
        Approved = this.Approved
        Rejected = this.Rejected }
      
  type PostInfo with
    member this.ToDto() =
      { PostDto.Slug = this.Slug.ToString()
        Title = this.Title.ToString()
        CreatedDate = this.CreatedDate
        CommentStats = this.CommentStats.ToDto() }

type GetAllPosts (
  config: Config,
  emailClient: EmailClient,
  logger : ILogger<GetAllPosts>,
  tableServiceClient: TableServiceClient
) =
  
  [<Function("Admin-Posts-GetAll")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "internal/posts")>] req : HttpRequestData,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.GetAll"
    ct |> (
      Auth.runIfAuthorized config logger req op
      <| cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request", op)
        let response = req.CreateResponse HttpStatusCode.OK

        let validRedirectUris = config.ValidRedirectUris |> List.map _.Uri
        
        // Dependencies
        let postDependencies =
          PostDependencies.live validRedirectUris emailClient config.Azure.EmailSender tableServiceClient logger
        let postQueries = PostQueries.withDependencies postDependencies
        
        let! postsResult = postQueries.GetAllPosts()
        let apiResponse, statusCode = postsResult |> QueryResult.toApiResponse (List.map _.ToDto())
          
        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
