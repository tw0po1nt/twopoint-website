namespace TwoPoint.Core.Deploys.Actions

open TwoPoint.Core.Deploys
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open IcedTasks

open System

// ====================================================================================================================
// Rebuild site
// ====================================================================================================================

[<RequireQualifiedAccess>]
module RebuildSite =

  // Output

  type Error = DispatchFailed of failed : (Repo * DependencyError) list
  type Event = Dispatched of Rebuild

  // Logic

  [<RequireQualifiedAccess>]
  module Logic =
    let rebuild (dispatchedDate : DateTime) (production : Result<Dispatch, DependencyError>) =
      { DispatchedDate = dispatchedDate
        Production =
          match production with
          | Ok dispatch -> ProductionDispatch.Dispatched dispatch
          | Error _ -> ProductionDispatch.Failed }

    let decide
      (repos : SiteRepos)
      (production : Result<Dispatch, DependencyError>)
      (staging : Result<Dispatch, DependencyError>)
      : Decision<unit, Error> =
      [ repos.Production, production; repos.Staging, staging ]
      |> List.choose (fun (repo, result) ->
        match result with
        | Ok _ -> None
        | Error error -> Some (repo, error))
      |> function
        | [] -> Decision.success ()
        | failed -> Decision.failure (DispatchFailed failed)

  // Action

  type Action = unit -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetSiteRepos: unit -> SiteRepos
    abstract member GetUtcNow: unit -> DateTime
    abstract member Dispatch: repo: Repo -> DependencyResult<Dispatch>
    abstract member SaveRebuild: rebuild: Rebuild -> DependencyResult<unit>

  let withDependencies (dependencies : Dependencies) : Action =
    fun () -> cancellableTask {
      let repos = dependencies.GetSiteRepos()
      let dispatchedDate = dependencies.GetUtcNow()
      let! production = dependencies.Dispatch repos.Production
      let! staging = dependencies.Dispatch repos.Staging
      let rebuild = Logic.rebuild dispatchedDate production

      match! dependencies.SaveRebuild rebuild with
      | Error error -> return ActionResult.failure (ActionError.Dependency error)
      | Ok () ->
        return
          Logic.decide repos production staging
          |> Result.map (fun () -> Event.Dispatched rebuild)
          |> Result.mapError ActionError.Logic
    }
