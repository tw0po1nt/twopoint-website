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
