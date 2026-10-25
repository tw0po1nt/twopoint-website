module TwoPoint.Core.Tests.DeployRuleTests

open TwoPoint.Core.Deploys
open TwoPoint.Core.Deploys.Queries
open TwoPoint.Core.Posts
open TwoPoint.Core.Util

open Expecto
open FsToolkit.ErrorHandling
open IcedTasks

open System
open System.Threading

let private at minutes = DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc).AddMinutes(float minutes)

let private production = { Owner = "tw0po1nt"; Name = "twopoint-website" }

let private run number state event created =
  { Repo = production
    Number = RunNumber number
    Event = event
    State = state
    CreatedDate = at created
    UpdatedDate = at created }

let private dispatched number = ProductionDispatch.Dispatched(Queued(RunNumber number))

let private change changed production : Change = { ChangedDate = at changed; Production = production }

let private runs latest latestSucceeded : ProductionRuns = { Latest = latest; LatestSucceeded = latestSucceeded }

let private stateAt now rebuild runs change = DeployRule.state (at now) runs rebuild change

let private expect expected actual = Expect.equal actual expected "The deploy state"

[<Tests>]
let tests =
  testList "The deploy rule" [
    test "a successful run with the run number of the change deploys it" {
      let done' = run 10L RunState.Succeeded WorkflowDispatch 1
      stateAt 5 None (runs (Some done') (Some done')) (change 0 (dispatched 10L)) |> expect Deployed
    }

    test "a successful run with a higher run number deploys the change" {
      let later = run 12L RunState.Succeeded WorkflowDispatch 3
      stateAt 5 None (runs (Some later) (Some later)) (change 0 (dispatched 10L)) |> expect Deployed
    }

    test "a successful push to main with a higher run number deploys the change" {
      let push = run 11L RunState.Succeeded Push 3
      stateAt 5 None (runs (Some push) (Some push)) (change 0 (dispatched 10L)) |> expect Deployed
    }

    test "a successful run with a lower run number does not deploy the change, and the run of the change is deploying" {
      let earlier = run 9L RunState.Succeeded WorkflowDispatch 0
      let own = run 10L RunState.Running WorkflowDispatch 1
      stateAt 5 None (runs (Some own) (Some earlier)) (change 0 (dispatched 10L)) |> expect Deploying
    }

    test "a change whose run is not in the table yet is deploying" {
      stateAt 5 None (runs None None) (change 0 (dispatched 10L)) |> expect Deploying
    }

    test "a failed run is a failed deploy" {
      let failed = run 10L RunState.Failed WorkflowDispatch 1
      stateAt 5 None (runs (Some failed) None) (change 0 (dispatched 10L)) |> expect DeployFailed
    }

    test "a run that a later run replaces is deploying while the later run waits" {
      let later = run 11L RunState.Waiting WorkflowDispatch 2
      stateAt 5 None (runs (Some later) None) (change 0 (dispatched 10L)) |> expect Deploying
    }

    test "a run that a later run replaces is deployed when the later run succeeds" {
      let later = run 11L RunState.Succeeded WorkflowDispatch 2
      stateAt 5 None (runs (Some later) (Some later)) (change 0 (dispatched 10L)) |> expect Deployed
    }

    test "a run that a later run replaces is a failed deploy when the later run fails" {
      let later = run 11L RunState.Failed WorkflowDispatch 2
      stateAt 5 None (runs (Some later) None) (change 0 (dispatched 10L)) |> expect DeployFailed
    }

    test "a canceled run is deploying until the grace ends, and then a failed deploy" {
      let replaced = { run 10L RunState.Canceled WorkflowDispatch 1 with UpdatedDate = at 2 }
      stateAt 3 None (runs (Some replaced) None) (change 0 (dispatched 10L)) |> expect Deploying
      stateAt 30 None (runs (Some replaced) None) (change 0 (dispatched 10L)) |> expect DeployFailed
    }

    test "a failed dispatch with no run after it is a failed deploy" {
      stateAt 5 None (runs None None) (change 0 ProductionDispatch.Failed) |> expect DeployFailed
    }

    test "a failed dispatch is deployed after a successful run that was created after the change" {
      let after = run 20L RunState.Succeeded Push 3
      stateAt 5 None (runs (Some after) (Some after)) (change 0 ProductionDispatch.Failed) |> expect Deployed
    }

    test "a failed dispatch is not deployed by a successful run that was created before the change" {
      let before = run 20L RunState.Succeeded Push -3
      stateAt 5 None (runs (Some before) (Some before)) (change 0 ProductionDispatch.Failed) |> expect DeployFailed
    }

    test "a failed dispatch is deploying while a run that was created after the change waits" {
      let waiting = run 20L RunState.Waiting Push 3
      stateAt 5 None (runs (Some waiting) None) (change 0 ProductionDispatch.Failed) |> expect Deploying
    }

    test "a dispatch with no run number is deploying until a run is created after the change" {
      let change = change 0 (ProductionDispatch.Dispatched QueuedWithoutRunNumber)
      stateAt 5 None (runs None None) change |> expect Deploying

      let after = run 20L RunState.Succeeded WorkflowDispatch 2
      stateAt 5 None (runs (Some after) (Some after)) change |> expect Deployed
    }

    test "a logged dispatch is deployed at the time of the change" {
      stateAt 0 None (runs None None) (change 0 (ProductionDispatch.Dispatched Logged)) |> expect Deployed
    }

    test "a later rebuild after a failed deploy is deploying, and then deployed" {
      let failed = run 10L RunState.Failed WorkflowDispatch 1
      let rebuild = { DispatchedDate = at 4; Production = dispatched 12L }
      stateAt 5 (Some rebuild) (runs (Some failed) None) (change 0 (dispatched 10L)) |> expect Deploying

      let rebuilt = run 12L RunState.Succeeded WorkflowDispatch 4
      stateAt 9 (Some rebuild) (runs (Some rebuilt) (Some rebuilt)) (change 0 (dispatched 10L)) |> expect Deployed
    }

    test "a failed rebuild does not change the state of a deployed change" {
      let live = run 10L RunState.Succeeded WorkflowDispatch 1
      let rebuild = { DispatchedDate = at 4; Production = ProductionDispatch.Failed }
      stateAt 5 (Some rebuild) (runs (Some live) (Some live)) (change 0 (dispatched 10L)) |> expect Deployed
    }

    test "a rebuild before the change does not replace the change" {
      let failed = run 10L RunState.Failed WorkflowDispatch 1
      let rebuild = { DispatchedDate = at -5; Production = dispatched 3L }
      stateAt 5 (Some rebuild) (runs (Some failed) None) (change 0 (dispatched 10L)) |> expect DeployFailed
    }

    test "a post with no recorded change is deployed" {
      let context = { Changes = Map.empty; Rebuild = None; Runs = runs None None }
      DeployRule.stateOf (at 5) context (Slug.Unsafe.create "old-post") |> expect Deployed
    }
  ]

type private FakeContext(context : DeployContext) =
  interface GetPostDeploy.Dependencies with
    member _.GetUtcNow () = at 5
    member _.GetDeployContext () = CancellableTaskResult.singleton context

[<Tests>]
let queryTests =
  testList "Get the deploy state of a post" [
    test "the state is the rule applied to the change of the slug" {
      let changes = Map.ofList [ Slug.Unsafe.create "my-post", change 0 ProductionDispatch.Failed ]
      let context = { Changes = changes; Rebuild = None; Runs = runs None None }
      let result = (GetPostDeploy.withDependencies (FakeContext context) "my-post" CancellationToken.None).Result
      Expect.equal result (Ok DeployFailed) "A failed deploy"
    }

    test "an invalid slug is a validation error" {
      let context = { Changes = Map.empty; Rebuild = None; Runs = runs None None }

      match (GetPostDeploy.withDependencies (FakeContext context) "Not A Slug" CancellationToken.None).Result with
      | Error(QueryError.Validation _) -> ()
      | other -> failtestf "Expected a validation error, got %A" other
    }
  ]
