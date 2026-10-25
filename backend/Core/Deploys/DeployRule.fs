namespace TwoPoint.Core.Deploys

open TwoPoint.Core.Posts

open System

[<RequireQualifiedAccess>]
module DeployRule =

  /// GitHub sends the webhooks of a replaced run and of the run that replaces it in no fixed order.
  let replacedGrace = TimeSpan.FromMinutes 2.

  let private includes (change : Change) (run : SiteRun) =
    match ProductionDispatch.runNumber change.Production with
    | Some number -> run.Number.Value >= number.Value
    | None -> run.CreatedDate > change.ChangedDate

  let private stateOfChange (now : DateTime) (runs : ProductionRuns) (change : Change) =
    match change.Production with
    | ProductionDispatch.Dispatched Logged -> Deployed
    | _ ->
      if runs.LatestSucceeded |> Option.exists (includes change) then
        Deployed
      else
        match runs.Latest |> Option.filter (includes change) with
        | Some run ->
          match run.State with
          | RunState.Waiting
          | RunState.Running -> Deploying
          | RunState.Succeeded -> Deployed
          | RunState.Failed -> DeployFailed
          | RunState.Canceled -> if now - run.UpdatedDate < replacedGrace then Deploying else DeployFailed
        | None ->
          match change.Production with
          | ProductionDispatch.Dispatched _ -> Deploying
          | ProductionDispatch.Failed -> DeployFailed

  let state (now : DateTime) (runs : ProductionRuns) (rebuild : Rebuild option) (change : Change) : DeployState =
    match stateOfChange now runs change, rebuild with
    | Deployed, _ -> Deployed
    | _, Some rebuild when rebuild.DispatchedDate > change.ChangedDate && rebuild.Production <> ProductionDispatch.Failed ->
      stateOfChange now runs (Change.ofRebuild rebuild)
    | own, _ -> own

  let stateOf (now : DateTime) (context : DeployContext) (slug : Slug) : DeployState =
    match Map.tryFind slug context.Changes with
    | Some change -> state now context.Runs context.Rebuild change
    | None -> Deployed
