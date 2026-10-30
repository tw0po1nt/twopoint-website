module TwoPoint.Admin.Tests.SiteDeployTests

open TwoPoint.Admin

open Expecto

let private run number state : Api.SiteRun =
  { Number = number
    State = state
    Url = $"https://github.com/tw0po1nt/twopoint-website/actions/runs/{number}"
    UpdatedDate = "2026-10-30T09:00:00Z" }

let private site deploy production staging : Api.SiteDeploy =
  { Deploy = deploy
    Production = production
    Staging = staging }

let private live = site Api.DeployState.Deployed (Some(run 40 Api.RunState.Succeeded)) None

let private started path =
  let model, _ = Shell.init path
  let model, _ = Shell.update (Shell.Session(Session.Started true)) model
  model

let private answered answer model =
  Shell.update (Shell.SiteDeploy(SiteDeploy.Answered answer)) model

[<Tests>]
let tests =
  testList "SiteDeploy" [
    test "a started session on the dashboard loads the deploy state of the site" {
      let model, _ = Shell.init "/"
      let _, effect = Shell.update (Shell.Session(Session.Started true)) model

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.LoadDashboard; Shell.LoadQueue; Shell.LoadSiteDeploy ]))
        "The first API calls"
    }

    test "a return to the dashboard loads the deploy state again" {
      let model = started "/posts"
      let model, effect = Shell.update (Shell.Router(Router.LinkFollowed "/")) model

      match effect with
      | Some(Shell.Navigate(_, calls)) -> Expect.equal calls [ Shell.LoadSiteDeploy ] "The deploy state"
      | other -> failtestf "Expected a navigation, got %A" other

      Expect.equal model.SiteDeploy.Status SiteDeploy.Waiting "The state waits"
    }

    test "a failed production run shows with a link to the run" {
      let failed = run 41 Api.RunState.Failed
      let model, effect = answered (Ok(site Api.DeployState.DeployFailed (Some failed) None)) (started "/")

      Expect.isNone effect "No poll after a failure"
      Expect.equal (SiteDeploy.state model.SiteDeploy) (Some Api.DeployState.DeployFailed) "Deploy failed"
      Expect.equal (SiteDeploy.productionFailure model.SiteDeploy) (Some failed) "The failed run"
      Expect.equal (SiteDeploy.productionFailureTitle failed) "The production run #41 failed." "The title"
      Expect.equal failed.Url "https://github.com/tw0po1nt/twopoint-website/actions/runs/41" "The link"
      Expect.isNone (SiteDeploy.stagingWarning model.SiteDeploy) "No staging warning"
    }

    test "a failed staging run shows only as a warning" {
      let failed = run 77 Api.RunState.Failed
      let model, _ = answered (Ok { live with Staging = Some failed }) (started "/")

      Expect.equal (SiteDeploy.state model.SiteDeploy) (Some Api.DeployState.Deployed) "The site is deployed"
      Expect.equal (SiteDeploy.stagingWarning model.SiteDeploy) (Some failed) "The staging warning"
      Expect.equal (SiteDeploy.stagingWarningTitle failed) "The staging run #77 failed." "The title"
      Expect.isNone (SiteDeploy.productionFailure model.SiteDeploy) "No production failure"
    }

    test "successful runs show no failure and no warning" {
      let model, _ = answered (Ok { live with Staging = Some(run 78 Api.RunState.Succeeded) }) (started "/")

      Expect.isNone (SiteDeploy.productionFailure model.SiteDeploy) "No production failure"
      Expect.isNone (SiteDeploy.stagingWarning model.SiteDeploy) "No staging warning"
    }

    test "a deploying site polls until the deploy ends" {
      let model = started "/"
      let generation = model.SiteDeploy.Generation
      let deploying = site Api.DeployState.Deploying (Some(run 42 Api.RunState.Running)) None

      let model, effect = answered (Ok deploying) model
      Expect.equal effect (Some(Shell.WaitToPollSiteDeploy generation)) "A timer"

      let model, effect = Shell.update (Shell.SiteDeploy(SiteDeploy.PollElapsed generation)) model
      Expect.equal effect (Some(Shell.Send [ Shell.PollSiteDeploy generation ])) "A poll"

      let failed = run 42 Api.RunState.Failed
      let model, effect =
        Shell.update (Shell.SiteDeploy(SiteDeploy.Polled(generation, Ok(site Api.DeployState.DeployFailed (Some failed) None)))) model

      Expect.isNone effect "The poll stops"
      Expect.equal (SiteDeploy.productionFailure model.SiteDeploy) (Some failed) "The failure shows"
    }

    test "a poll timer away from the dashboard sends nothing" {
      let model = started "/"
      let generation = model.SiteDeploy.Generation
      let model, _ = answered (Ok(site Api.DeployState.Deploying None None)) model
      let model, _ = Shell.update (Shell.Router(Router.LinkFollowed "/posts")) model
      let _, effect = Shell.update (Shell.SiteDeploy(SiteDeploy.PollElapsed generation)) model

      Expect.isNone effect "No poll"
    }

    test "a poll answer of an earlier generation is ignored" {
      let model, _ = answered (Ok live) (started "/")
      let stale = model.SiteDeploy.Generation - 1
      let failed = site Api.DeployState.DeployFailed (Some(run 41 Api.RunState.Failed)) None
      let model, _ = Shell.update (Shell.SiteDeploy(SiteDeploy.Polled(stale, Ok failed))) model

      Expect.equal (SiteDeploy.state model.SiteDeploy) (Some Api.DeployState.Deployed) "The state stays"
    }

    test "a started rebuild reads the deploy state again with a new generation, and keeps the shown state" {
      let model, _ = answered (Ok live) (started "/")
      let before = model.SiteDeploy.Generation
      let model, _ = Shell.update (Shell.Rebuild Rebuild.Clicked) model
      let model, effect = Shell.update (Shell.Rebuild(Rebuild.Answered(Ok(Api.Queued 43)))) model

      Expect.equal model.SiteDeploy.Generation (before + 1) "A new generation"
      Expect.equal effect (Some(Shell.Send [ Shell.PollSiteDeploy(before + 1) ])) "A read of the deploy state"
      Expect.equal (SiteDeploy.state model.SiteDeploy) (Some Api.DeployState.Deployed) "The state shows until the answer"
    }

    test "an unanswered poll waits and polls again" {
      let model, _ = answered (Ok(site Api.DeployState.Deploying None None)) (started "/")
      let generation = model.SiteDeploy.Generation
      let _, effect = Shell.update (Shell.SiteDeploy(SiteDeploy.Polled(generation, Error Api.Unanswered))) model

      Expect.equal effect (Some(Shell.WaitToPollSiteDeploy generation)) "Another timer"
    }

    test "a load that fails shows the reason" {
      let model, _ = answered (Error Api.Invalid) (started "/")

      Expect.isSome (SiteDeploy.reason model.SiteDeploy) "The reason"
      Expect.isNone (SiteDeploy.state model.SiteDeploy) "No state"
    }

    test "a refused token offers another account" {
      let model, _ = answered (Error Api.Refused) (started "/")

      Expect.equal model.Session.Status Session.Refused "The banner offers another account"
    }
  ]
