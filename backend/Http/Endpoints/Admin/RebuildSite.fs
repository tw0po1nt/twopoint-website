namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Deploys
open TwoPoint.Core.Deploys.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Deploys
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Core
open Azure.Data.Tables
open Azure.Security.KeyVault.Keys.Cryptography
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

  let dispatchWith (deploy : DeployConfig) : Result<Repo -> DependencyResult<Dispatch>, string> =
    if environment.IsDevelopment() then
      Ok (LoggedDispatch(logger).Dispatch)
    else
      match deploy.GitHubApp with
      | None -> Error "The 'Deploy:GitHubApp' configuration section is missing"
      | Some app ->
        let signer = CryptographyClient(app.SigningKeyUri, credential)
        let app = { GitHubApp.ClientId = app.ClientId; SigningKey = app.SigningKeyUri }
        Ok (GitHubDispatch(httpClientFactory.CreateClient(), signer, app, logger).Dispatch)

  let rebuild (deploy : DeployConfig) dispatch = cancellableTask {
    let rebuildDb = RebuildDb(tableServiceClient, logger)

    let rebuildSite = RebuildSite.withDependencies { new RebuildSite.Dependencies with
      member _.GetSiteRepos () = { Production = deploy.ProductionRepo; Staging = deploy.StagingRepo }
      member _.GetUtcNow () = DateTime.UtcNow
      member _.Dispatch repo = dispatch repo
      member _.SaveRebuild rebuild = rebuildDb.SaveRebuild rebuild
    }

    match! rebuildSite () with
    | Ok (RebuildSite.Rebuilt production) ->
      let data = { Dispatch = Dispatch.name production; ProductionRun = Dispatch.runNumber production |> Option.map _.Value }
      let apiResponse = { Success = true; Message = None; Data = Some data }
      return apiResponse, HttpStatusCode.OK
    | Error (ActionError.Logic (RebuildSite.DispatchFailed failed)) ->
      for repo, error in failed do
        logger.LogError("The dispatch of site.yml to {repo} failed: {error}", repo, error.DebugMessage)
      let repos = failed |> List.map (fst >> string) |> String.concat " and "
      return failure $"The dispatch to {repos} failed." HttpStatusCode.BadGateway
    | Error actionError ->
      return failure (actionError.ToString()) HttpStatusCode.InternalServerError
  }

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

        let! apiResponse, statusCode =
          match config.Deploy with
          | None ->
            logger.LogError("Unable to rebuild the site. The 'Deploy' configuration section is missing")
            CancellableTask.singleton (
              failure "The 'Deploy' configuration section is missing" HttpStatusCode.InternalServerError
            )
          | Some deploy ->
            match dispatchWith deploy with
            | Error message ->
              logger.LogError("Unable to rebuild the site. {message}", message)
              CancellableTask.singleton (failure message HttpStatusCode.InternalServerError)
            | Ok dispatch -> rebuild deploy dispatch

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
