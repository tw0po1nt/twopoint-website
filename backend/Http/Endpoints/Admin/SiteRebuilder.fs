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

type SiteRebuildOutcome =
  | NotConfigured of reason: string
  | Attempted of ActionResult<RebuildSite.Event, RebuildSite.Error>

/// Dispatches `site.yml` in the two site repos. The local backend logs each dispatch and sends nothing.
type SiteRebuilder(
  config : Config,
  credential : TokenCredential,
  environment : IHostEnvironment,
  httpClientFactory : IHttpClientFactory,
  logger : ILogger,
  tableServiceClient : TableServiceClient
) =

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

  let rebuild (deploy : DeployConfig) (dispatch : Repo -> DependencyResult<Dispatch>) = cancellableTask {
    let rebuildDb = RebuildDb(tableServiceClient, logger)

    let rebuildSite = RebuildSite.withDependencies { new RebuildSite.Dependencies with
      member _.GetSiteRepos () = { Production = deploy.ProductionRepo; Staging = deploy.StagingRepo }
      member _.GetUtcNow () = DateTime.UtcNow
      member _.Dispatch repo = dispatch repo
      member _.SaveRebuild rebuild = rebuildDb.SaveRebuild rebuild
    }

    return! rebuildSite ()
  }

  member _.Rebuild () : CancellableTask<SiteRebuildOutcome> =
    match config.Deploy with
    | None ->
      logger.LogError("Unable to rebuild the site. The 'Deploy' configuration section is missing")
      CancellableTask.singleton (NotConfigured "The 'Deploy' configuration section is missing")
    | Some deploy ->
      match dispatchWith deploy with
      | Error message ->
        logger.LogError("Unable to rebuild the site. {message}", message)
        CancellableTask.singleton (NotConfigured message)
      | Ok dispatch -> rebuild deploy dispatch |> CancellableTask.map Attempted

  member this.RebuildAsDependency () : DependencyResult<unit> = cancellableTask {
    let failure (message : string) : Result<unit, DependencyError> =
      Error { Dependency = Dependency.GitHub; Type = Unknown (exn message) }

    match! this.Rebuild () with
    | NotConfigured reason -> return failure reason
    | Attempted (Ok _) -> return Ok ()
    | Attempted (Error (ActionError.Logic (RebuildSite.DispatchFailed failed))) ->
      let repos = failed |> List.map (fst >> string) |> String.concat " and "
      for repo, error in failed do
        logger.LogError("The dispatch of site.yml to {repo} failed: {error}", repo, error.DebugMessage)
      return failure $"The dispatch to {repos} failed."
    | Attempted (Error (ActionError.Dependency error)) -> return Error error
    | Attempted (Error (ActionError.Validation messages)) -> return failure (String.concat ", " messages)
  }
