namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Util
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Data.Tables
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System
open System.Net
open System.Threading

module GetPostDeployQuery = TwoPoint.Core.Deploys.Queries.GetPostDeploy

type PostDeployDto = { Deploy : string }

type GetPostDeploy(config : Config, logger : ILogger<GetPostDeploy>, tableServiceClient : TableServiceClient) =

  [<Function("Admin-Posts-GetDeploy")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "internal/posts/{slug}/deploy")>] req : HttpRequestData,
    context : FunctionContext,
    slug : string,
    ct : CancellationToken
  ) =
    let op = "Admin.Posts.GetDeploy"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request with slug '{slug}'", op, slug)
        let response = req.CreateResponse HttpStatusCode.OK
        let store = DeployStore(config, tableServiceClient, logger)

        let getPostDeploy = GetPostDeployQuery.withDependencies { new GetPostDeployQuery.Dependencies with
          member _.GetUtcNow () = DateTime.UtcNow
          member _.GetDeployContext () = store.GetDeployContext()
        }

        let! result = getPostDeploy slug
        let apiResponse, statusCode = result |> QueryResult.toApiResponse (fun state -> { Deploy = state.Name })

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
