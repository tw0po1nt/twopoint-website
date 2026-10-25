namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Http
open TwoPoint.Http.Endpoints
open TwoPoint.Core.Posts
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Posts

open Azure.Communication.Email
open Azure.Data.Tables
open Azure.Storage.Blobs
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
    State : string
    PendingEdit : bool
    PublishDate : Nullable<DateTime>
    CommentStats : PostCommentStatsDto }

[<AutoOpen>]
module PostTypesExt =

  type PostCommentStats with
    member this.ToDto() =
      { PostCommentStatsDto.New = this.New
        Approved = this.Approved
        Rejected = this.Rejected }

  type PostState with
    member this.Name =
      match this with
      | Draft -> "draft"
      | Published -> "published"
      | Upcoming -> "upcoming"

  type PostListing with
    member this.ToDto() =
      { PostDto.Slug = this.Slug.ToString()
        Title = this.Title
        State = this.State.Name
        PendingEdit = this.HasPendingEdit
        PublishDate = Option.toNullable this.PublishDate
        CommentStats = this.CommentStats.ToDto() }

module GetAllPostsQuery = TwoPoint.Core.Posts.Queries.GetAllPosts

type GetAllPosts (
  config: Config,
  emailClient: EmailClient,
  logger : ILogger<GetAllPosts>,
  tableServiceClient: TableServiceClient,
  postStoreClient: BlobServiceClient
) =

  [<Function("Admin-Posts-GetAll")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "internal/posts")>] req : HttpRequestData,
    context : FunctionContext,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.GetAll"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request", op)
        let response = req.CreateResponse HttpStatusCode.OK
        let validRedirectUris = config.ValidRedirectUris |> List.map _.Uri

        // Dependencies
        let postDependencies = PostDependencies(
          validRedirectUris,
          emailClient,
          config.Azure.EmailSender,
          tableServiceClient,
          logger
        )
        let postStore = PostStore(postStoreClient, logger)
        let getAllPosts = TwoPoint.Core.Posts.Queries.GetAllPosts.withDependencies {
          new GetAllPostsQuery.Dependencies with

          member _.GetUtcNow () = DateTime.UtcNow
          member _.GetPublishedPosts () = postStore.GetPublishedPosts()
          member _.GetWorkingPosts () = postStore.GetWorkingPosts()
          member _.GetPostsInTable () = postDependencies.GetAllPosts()
        }

        let! postsResult = getAllPosts()
        let apiResponse, statusCode = postsResult |> QueryResult.toApiResponse (List.map _.ToDto())

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
