namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Deploys
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Deploys
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

module GetSiteDeployQuery = TwoPoint.Core.Deploys.Queries.GetSiteDeploy

type SiteRunDto =
  { Number : int64
    State : string
    Url : string
    UpdatedDate : string }

type SiteDeployDto =
  { Deploy : string
    Production : SiteRunDto option
    Staging : SiteRunDto option }

module SiteDeployDto =

  let private run (run : SiteRun) =
    { Number = run.Number.Value
      State = DeployRows.stateName run.State
      Url = string run.Url
      UpdatedDate = ChangeColumns.dateText run.UpdatedDate }

  let ofSiteDeploy (deploy : GetSiteDeployQuery.SiteDeploy) =
    { Deploy = deploy.State.Name
      Production = deploy.Production |> Option.map run
      Staging = deploy.Staging |> Option.map run }

type GetSiteDeploy(config : Config, logger : ILogger<GetSiteDeploy>, tableServiceClient : TableServiceClient) =

  [<Function("Admin-Deploys-Get")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "internal/deploys")>] req : HttpRequestData,
    context : FunctionContext,
    ct : CancellationToken
  ) =
    let op = "Admin.Deploys.Get"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request", op)
        let response = req.CreateResponse HttpStatusCode.OK
        let store = DeployStore(config, tableServiceClient, logger)

        let getSiteDeploy = GetSiteDeployQuery.withDependencies { new GetSiteDeployQuery.Dependencies with
          member _.GetUtcNow () = DateTime.UtcNow
          member _.GetDeployContext () = store.GetDeployContext()
          member _.GetLatestStagingRun () = store.GetLatestStagingRun()
        }

        let! result = getSiteDeploy ()
        let apiResponse, statusCode = result |> QueryResult.toApiResponse SiteDeployDto.ofSiteDeploy

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
