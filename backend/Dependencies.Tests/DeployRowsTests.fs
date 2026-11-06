module TwoPoint.Dependencies.Tests.DeployRowsTests

open TwoPoint.Core.Deploys
open TwoPoint.Core.Posts
open TwoPoint.Dependencies.Deploys

open Expecto

open System

let private repo = { Owner = "tw0po1nt"; Name = "twopoint-website" }

let private run =
  { Repo = repo
    Number = RunNumber 41L
    Attempt = RunAttempt 2
    Event = WorkflowDispatch
    State = RunState.Running
    CreatedDate = DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc)
    UpdatedDate = DateTime(2026, 3, 1, 9, 5, 0, DateTimeKind.Utc)
    Url = Uri "https://github.com/tw0po1nt/twopoint-website/actions/runs/9001" }

[<Tests>]
let tests =
  testList "DeployRows" [
    test "a newer run has a row key that sorts first" {
      let keys = [ 9L; 10L; 100L ] |> List.map (RunNumber >> DeployRows.runRow)
      Expect.equal (List.sort keys) (List.rev keys) "The newest run is first"
    }

    test "a run keeps its fields through a table entity" {
      Expect.equal (DeployRows.runEntity run |> DeployRows.tryRun repo) (Some run) "The same run"
    }

    test "a run that was stored with no link links to the runs of site.yml" {
      let entity = DeployRows.runEntity run
      entity.Remove "Url" |> ignore

      Expect.equal
        (DeployRows.tryRun repo entity |> Option.map _.Url)
        (Some(Uri "https://github.com/tw0po1nt/twopoint-website/actions/workflows/site.yml"))
        "The runs of site.yml"
    }

    test "a run that was stored with no attempt is the first attempt" {
      let entity = DeployRows.runEntity run
      entity.Remove "Attempt" |> ignore

      Expect.equal (DeployRows.tryRun repo entity |> Option.map _.Attempt) (Some(RunAttempt 1)) "The first attempt"
    }

    test "a run with an unknown state is not read" {
      let entity = DeployRows.runEntity run
      entity["State"] <- "unknown"
      Expect.equal (DeployRows.tryRun repo entity) None "No run"
    }

    test "a change keeps its run number through a table entity" {
      let change =
        { Slug = Slug.Unsafe.create "my-post"
          Change = { ChangedDate = run.CreatedDate; Production = ProductionDispatch.Dispatched(Queued(RunNumber 7L)) } }

      Expect.equal
        (DeployRows.changeEntity change |> DeployRows.tryChange)
        (Some(change.Slug, change.Change))
        "The same change"
    }

    test "a change with a failed dispatch has no run number" {
      let change =
        { Slug = Slug.Unsafe.create "my-post"
          Change = { ChangedDate = run.CreatedDate; Production = ProductionDispatch.Failed } }

      Expect.equal
        (DeployRows.changeEntity change |> DeployRows.tryChange)
        (Some(change.Slug, change.Change))
        "The same change"
    }
  ]
