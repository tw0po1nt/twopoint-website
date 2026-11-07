namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Posts
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Posts
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Communication.Email
open Azure.Data.Tables
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System.Net
open System.Threading

type PostedCommentDto =
  { PostSlug : string
    PostTitle : string
    Comment : AdminCommentDto }

[<AutoOpen>]
module PostedCommentExt =

  type AdminPostedComment with
    member this.ToDto() =
      { PostedCommentDto.PostSlug = this.Post.ToString()
        PostTitle = this.PostTitle.ToString()
        Comment = this.Comment.ToDto() }

module GetModerationQueueQuery = TwoPoint.Core.Posts.Queries.GetModerationQueue

type GetModerationQueue (
  config: Config,
  emailClient: EmailClient,
  logger : ILogger<GetModerationQueue>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Admin-Comments-GetModerationQueue")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "internal/comments")>] req : HttpRequestData,
    context : FunctionContext,
    ct : CancellationToken
  ) =
    let op = "Admin.Comments.GetModerationQueue"
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
        let getModerationQueue = GetModerationQueueQuery.withDependencies {
          new GetModerationQueueQuery.Dependencies with

          member _.GetCommentsOnAllPosts approvals = postDependencies.GetCommentsOnAllPosts approvals
          member _.Stage = config.Stage
        }

        let! queueResult = getModerationQueue()
        let apiResponse, statusCode = queueResult |> QueryResult.toApiResponse (List.map _.ToDto())

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
