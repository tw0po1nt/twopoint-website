namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Deploys
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Deploys
open TwoPoint.Http

open Azure.Data.Tables
open FsToolkit.ErrorHandling
open IcedTasks
open Microsoft.Extensions.Logging

/// The local backend has no `Deploy` section, and it dispatches nothing, so each change there is deployed.
type DeployStore(config : Config, tableServiceClient : TableServiceClient, logger : ILogger) =

  let noRuns = { Latest = None; LatestSucceeded = None }

  member _.GetDeployContext () : DependencyResult<DeployContext> =
    match config.Deploy with
    | Some deploy -> DeployDb(tableServiceClient, logger).GetDeployContext deploy.ProductionRepo
    | None -> CancellableTaskResult.singleton { Changes = Map.empty; Rebuild = None; Runs = noRuns }
