namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Util
open TwoPoint.Dependencies.Posts
open TwoPoint.Dependencies.Subscriptions
open TwoPoint.Http
open TwoPoint.Http.Endpoints

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

module GetDashboardQuery = TwoPoint.Core.Dashboard.Queries.GetDashboard

type DashboardDto =
  { Drafts : int
    PendingEdits : int
    DeliveriesToStart : int
    ActiveSubscriptions : int
    NewSubscriptions : int
    CanceledSubscriptions : int }

module DashboardDto =

  let ofCounts (counts : GetDashboardQuery.Counts) =
    { Drafts = counts.Drafts
      PendingEdits = counts.PendingEdits
      DeliveriesToStart = counts.DeliveriesToStart
      ActiveSubscriptions = counts.ActiveSubscriptions
      NewSubscriptions = counts.NewSubscriptions
      CanceledSubscriptions = counts.CanceledSubscriptions }

type GetDashboard (
  config: Config,
  emailClient: EmailClient,
  logger : ILogger<GetDashboard>,
  tableServiceClient: TableServiceClient,
  postStoreClient: BlobServiceClient,
  texts : BlobTextCache
) =

  [<Function("Admin-Dashboard-Get")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "internal/dashboard")>] req : HttpRequestData,
    context : FunctionContext,
    ct : CancellationToken
  ) =
    let op = "Admin.Dashboard.Get"
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
        let postStore = PostStore(postStoreClient, logger, texts)
        let subscriptionDependencies = SubscriptionDependencies(tableServiceClient, logger)
        let getDashboard = GetDashboardQuery.withDependencies {
          new GetDashboardQuery.Dependencies with

          member _.GetUtcNow () = DateTime.UtcNow
          member _.GetPublishedPosts () = postStore.GetPublishedPosts()
          member _.GetWorkingPosts () = postStore.GetWorkingPosts()
          member _.GetPostsInTable () = postDependencies.GetAllPosts()
          member _.GetDeployContext () = DeployStore(config, tableServiceClient, logger).GetDeployContext()
          member _.GetMigratedPosts () = postStore.GetMigratedPosts()
          member _.GetDeliveryStarts () = DeliveryStartDb(tableServiceClient, logger).GetDeliveryStarts()
          member _.GetAllSubscriptions () = subscriptionDependencies.GetAllSubscriptions()
        }

        let! result = getDashboard ()
        let apiResponse, statusCode = result |> QueryResult.toApiResponse DashboardDto.ofCounts

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
