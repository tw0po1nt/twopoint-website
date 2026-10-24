namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Util
open TwoPoint.Dependencies.Posts
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Communication.Email
open Azure.Data.Tables
open FirebaseAdmin.Messaging
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System.Net
open System.Threading

module GetAllCommentersQuery = TwoPoint.Core.Posts.Queries.GetAllCommenters

type GetAllCommenters (
  config: Config,
  emailClient: EmailClient,
  messaging : FirebaseMessaging,
  logger : ILogger<GetAllCommenters>,
  tableServiceClient: TableServiceClient
) =

  [<Function("Admin-Commenters-GetAll")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "internal/commenters")>] req : HttpRequestData,
    context : FunctionContext,
    ct : CancellationToken
  ) =
    let op = "Admin.Commenters.GetAll"
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
          messaging,
          tableServiceClient,
          logger
        )
        let getAllCommenters = GetAllCommentersQuery.withDependencies {
          new GetAllCommentersQuery.Dependencies with

          member _.GetAllCommenters () = postDependencies.GetAllCommenters()
        }

        let! commentersResult = getAllCommenters()
        let apiResponse, statusCode = commentersResult |> QueryResult.toApiResponse (List.map _.ToDto())

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
