namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Deploys
open TwoPoint.Core.Deploys.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Deploys
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Core
open Azure.Data.Tables
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

open System
open System.Net
open System.Net.Http
open System.Threading

type RebuildSiteResponse =
  { Dispatch : string
    ProductionRun : int64 option }

type RebuildSite(
  config : Config,
  credential : TokenCredential,
  environment : IHostEnvironment,
  httpClientFactory : IHttpClientFactory,
  logger : ILogger<RebuildSite>,
  tableServiceClient : TableServiceClient
) =

  let failure (message : string) statusCode : ApiResponse<RebuildSiteResponse> * HttpStatusCode =
    { Success = false; Message = Some message; Data = None }, statusCode

  [<Function("Admin-Deploys-Rebuild")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "internal/deploys")>] req : HttpRequestData,
    context : FunctionContext,
    ct : CancellationToken
  ) =
    let op = "Admin.Deploys.Rebuild"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        logger.LogInformation("Processing '{op}' request", op)
        let response = req.CreateResponse HttpStatusCode.OK
        let rebuilder = SiteRebuilder(config, credential, environment, httpClientFactory, logger, tableServiceClient)

        let! apiResponse, statusCode = cancellableTask {
          match! rebuilder.Rebuild () with
          | NotConfigured problem -> return failure (SiteConfigProblem.text problem) HttpStatusCode.InternalServerError
          | Attempted (Ok (RebuildSite.Rebuilt production)) ->
            let data = { Dispatch = Dispatch.name production; ProductionRun = Dispatch.runNumber production |> Option.map _.Value }
            return { Success = true; Message = None; Data = Some data }, HttpStatusCode.OK
          | Attempted (Error (ActionError.Logic (RebuildSite.DispatchFailed failed))) ->
            for repo, error in failed do
              logger.LogError("The dispatch of site.yml to {repo} failed: {error}", repo, error.DebugMessage)
            let repos = failed |> List.map (fst >> string) |> String.concat " and "
            return failure $"The dispatch to {repos} failed." HttpStatusCode.BadGateway
          | Attempted (Error actionError) ->
            return failure (actionError.ToString()) HttpStatusCode.InternalServerError
        }

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
