namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Deploys
open TwoPoint.Core.Deploys.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Deploys
open TwoPoint.Http

open Azure.Core
open Azure.Data.Tables
open Azure.Security.KeyVault.Keys.Cryptography
open IcedTasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

open System
open System.Net.Http

type SiteConfigProblem =
  | DeploySectionMissing
  | GitHubAppSectionMissing

module SiteConfigProblem =

  let text (problem : SiteConfigProblem) =
    match problem with
    | DeploySectionMissing -> "The 'Deploy' configuration section is missing"
    | GitHubAppSectionMissing -> "The 'Deploy:GitHubApp' configuration section is missing"

type SiteRebuildOutcome =
  | NotConfigured of SiteConfigProblem
  | Attempted of ActionResult<RebuildSite.Event, RebuildSite.Error>

type SiteRebuilder(
  config : Config,
  credential : TokenCredential,
  environment : IHostEnvironment,
  httpClientFactory : IHttpClientFactory,
  logger : ILogger,
  tableServiceClient : TableServiceClient
) =

  let configured () : Result<DeployConfig * (Repo -> DependencyResult<Dispatch>), SiteConfigProblem> =
    match config.Deploy with
    | None -> Error DeploySectionMissing
    | Some deploy when environment.IsDevelopment() -> Ok (deploy, LoggedDispatch(logger).Dispatch)
    | Some deploy ->
      match deploy.GitHubApp with
      | None -> Error GitHubAppSectionMissing
      | Some app ->
        let signer = CryptographyClient(app.SigningKeyUri, credential)
        let app = { GitHubApp.ClientId = app.ClientId; SigningKey = app.SigningKeyUri }
        Ok (deploy, GitHubDispatch(httpClientFactory.CreateClient(), signer, app, logger).Dispatch)

  let dependencyError (problem : SiteConfigProblem) : DependencyError =
    { Dependency = Dependency.GitHub; Type = Unknown (exn (SiteConfigProblem.text problem)) }

  member _.Rebuild () : CancellableTask<SiteRebuildOutcome> =
    match configured () with
    | Error problem ->
      logger.LogError("Unable to rebuild the site. {message}", SiteConfigProblem.text problem)
      CancellableTask.singleton (NotConfigured problem)
    | Ok (deploy, dispatch) -> cancellableTask {
      let rebuildDb = RebuildDb(tableServiceClient, logger)

      let rebuildSite = RebuildSite.withDependencies { new RebuildSite.Dependencies with
        member _.GetSiteRepos () = { Production = deploy.ProductionRepo; Staging = deploy.StagingRepo }
        member _.GetUtcNow () = DateTime.UtcNow
        member _.Dispatch repo = dispatch repo
        member _.SaveRebuild rebuild = rebuildDb.SaveRebuild rebuild
      }

      let! result = rebuildSite ()
      return Attempted result
    }

  member _.RecordChange (change : PostChange) : DependencyResult<unit> =
    DeployDb(tableServiceClient, logger).RecordChange change

  member _.Dispatch () : DependencyResult<SiteDispatch> =
    match configured () with
    | Error problem ->
      logger.LogError("Unable to dispatch the site. {message}", SiteConfigProblem.text problem)
      CancellableTask.singleton (Error (dependencyError problem))
    | Ok (deploy, dispatch) -> cancellableTask {
      let dispatchedDate = DateTime.UtcNow
      let! production = dispatch deploy.ProductionRepo
      let! staging = dispatch deploy.StagingRepo

      for repo, result in [ deploy.ProductionRepo, production; deploy.StagingRepo, staging ] do
        match result with
        | Error error -> logger.LogError("The dispatch of site.yml to {repo} failed: {error}", repo, error.DebugMessage)
        | Ok _ -> ()

      match! RebuildDb(tableServiceClient, logger).SaveRebuild(RebuildSite.Logic.rebuild dispatchedDate production) with
      | Error error -> logger.LogError("The rebuild row was not saved: {error}", error.DebugMessage)
      | Ok () -> ()

      return Ok({ Production = production; Staging = staging } : SiteDispatch)
    }
