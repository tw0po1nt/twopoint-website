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
    Attempt = RunAttempt 1
    Event = event
    State = state
    CreatedDate = at created
    UpdatedDate = at created
    Url = Uri $"https://github.com/tw0po1nt/twopoint-website/actions/runs/{number}" }

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

    test "a dispatch with no run number is deploying until a run is created after the change, also a run that the dispatch queued" {
      let change = change 0 (ProductionDispatch.Dispatched QueuedWithoutRunNumber)
      stateAt 5 None (runs None None) change |> expect Deploying

      let after = run 20L RunState.Succeeded WorkflowDispatch 1
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

let private siteAt now changes rebuild runs =
  DeployRule.siteState (at now) { Changes = Map.ofList changes; Rebuild = rebuild; Runs = runs }

[<Tests>]
let siteTests =
  testList "The deploy state of the site" [
    test "a site with no change and no run is deployed" {
      siteAt 5 [] None (runs None None) |> expect Deployed
    }

    test "the newest change gives the state while its run deploys" {
      let live = run 10L RunState.Succeeded WorkflowDispatch 1
      let older = Slug.Unsafe.create "older", change 0 (dispatched 10L)
      let newer = Slug.Unsafe.create "newer", change 3 (dispatched 11L)
      siteAt 5 [ older; newer ] None (runs (Some live) (Some live)) |> expect Deploying
    }

    test "a failed dispatch of the newest change is a failed deploy" {
      let live = run 10L RunState.Succeeded WorkflowDispatch 1
      siteAt 5 [ Slug.Unsafe.create "my-post", change 3 ProductionDispatch.Failed ] None (runs (Some live) (Some live))
      |> expect DeployFailed
    }

    test "a rebuild after the newest change gives the state" {
      let live = run 10L RunState.Succeeded WorkflowDispatch 1
      let rebuild = { DispatchedDate = at 4; Production = dispatched 11L }
      siteAt 5 [ Slug.Unsafe.create "my-post", change 0 (dispatched 10L) ] (Some rebuild) (runs (Some live) (Some live))
      |> expect Deploying
    }

    test "a failed push run after the deployed changes is a failed deploy" {
      let live = run 10L RunState.Succeeded WorkflowDispatch 1
      let push = run 11L RunState.Failed Push 3
      siteAt 5 [ Slug.Unsafe.create "my-post", change 0 (dispatched 10L) ] None (runs (Some push) (Some live))
      |> expect DeployFailed
    }

    test "a running push run after the deployed changes is deploying" {
      let live = run 10L RunState.Succeeded WorkflowDispatch 1
      let push = run 11L RunState.Running Push 3
      siteAt 5 [ Slug.Unsafe.create "my-post", change 0 (dispatched 10L) ] None (runs (Some push) (Some live))
      |> expect Deploying
    }

    test "a successful run after a failed run is deployed" {
      let live = run 12L RunState.Succeeded WorkflowDispatch 4
      let rebuild = { DispatchedDate = at 4; Production = dispatched 12L }
      siteAt 5 [ Slug.Unsafe.create "my-post", change 0 (dispatched 11L) ] (Some rebuild) (runs (Some live) (Some live))
      |> expect Deployed
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

let private staging = { Owner = "tw0po1nt"; Name = "twopoint-website-staging" }

type private FakeSite(context : DeployContext, staging : SiteRun option) =
  interface GetSiteDeploy.Dependencies with
    member _.GetUtcNow () = at 5
    member _.GetDeployContext () = CancellableTaskResult.singleton context
    member _.GetLatestStagingRun () = CancellableTaskResult.singleton staging

[<Tests>]
let siteQueryTests =
  testList "Get the deploy state of the site" [
    test "the answer has the state of the site and the latest run of each repo" {
      let failed = run 11L RunState.Failed WorkflowDispatch 3
      let live = run 10L RunState.Succeeded WorkflowDispatch 1
      let stagingRun = { run 30L RunState.Failed Push 2 with Repo = staging }
      let context = { Changes = Map.empty; Rebuild = None; Runs = runs (Some failed) (Some live) }

      let result = (GetSiteDeploy.withDependencies (FakeSite(context, Some stagingRun)) () CancellationToken.None).Result

      Expect.equal
        result
        (Ok { GetSiteDeploy.State = DeployFailed; GetSiteDeploy.Production = Some failed; GetSiteDeploy.Staging = Some stagingRun })
        "The failed production run and the failed staging run"
    }
  ]
