namespace TwoPoint.Core.Deploys.Queries

open TwoPoint.Core.Deploys
open TwoPoint.Core.Posts
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

open System

[<RequireQualifiedAccess>]
module GetPostDeploy =

  type Query = string -> CancellableTask<QueryResult<DeployState>>

  type Dependencies =
    abstract member GetUtcNow: unit -> DateTime
    abstract member GetDeployContext: unit -> DependencyResult<DeployContext>

  let withDependencies (dependencies : Dependencies) : Query =
    fun slug -> cancellableTaskResult {
      let! slug = slug |> Slug.create |> Result.mapError QueryError.Validation
      let! context = dependencies.GetDeployContext() |> CancellableTaskResult.mapError QueryError.Dependency
      return DeployRule.stateOf (dependencies.GetUtcNow()) context slug
    }

[<RequireQualifiedAccess>]
module GetSiteDeploy =

  type SiteDeploy =
    { State : DeployState
      Production : SiteRun option
      Staging : SiteRun option }

  type Query = unit -> CancellableTask<QueryResult<SiteDeploy>>

  type Dependencies =
    abstract member GetUtcNow: unit -> DateTime
    abstract member GetDeployContext: unit -> DependencyResult<DeployContext>
    abstract member GetLatestStagingRun: unit -> DependencyResult<SiteRun option>

  let withDependencies (dependencies : Dependencies) : Query =
    fun () -> cancellableTaskResult {
      let! context = dependencies.GetDeployContext() |> CancellableTaskResult.mapError QueryError.Dependency
      let! staging = dependencies.GetLatestStagingRun() |> CancellableTaskResult.mapError QueryError.Dependency

      return
        { State = DeployRule.siteState (dependencies.GetUtcNow()) context
          Production = context.Runs.Latest
          Staging = staging }
    }
