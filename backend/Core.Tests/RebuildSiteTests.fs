module TwoPoint.Core.Tests.RebuildSiteTests

open TwoPoint.Core.Deploys
open TwoPoint.Core.Deploys.Actions
open TwoPoint.Core.Util

open Expecto
open FsToolkit.ErrorHandling
open IcedTasks

open System
open System.Threading

let private utcNow = DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
let private production = { Owner = "tw0po1nt"; Name = "twopoint-website" }
let private staging = { Owner = "tw0po1nt"; Name = "twopoint-website-staging" }
let private refused = { Dependency = Dependency.GitHub; Type = Unknown (exn "403 Forbidden") }

type private FakeDependencies(answer : Repo -> Result<Dispatch, DependencyError>) =
  let dispatched = ResizeArray<Repo>()
  let saved = ResizeArray<Rebuild>()

  member _.Dispatched = List.ofSeq dispatched
  member _.Saved = List.ofSeq saved

  interface RebuildSite.Dependencies with
    member _.GetSiteRepos () = { Production = production; Staging = staging }
    member _.GetUtcNow () = utcNow
    member _.Dispatch repo =
      dispatched.Add repo
      CancellableTask.singleton (answer repo)
    member _.SaveRebuild rebuild =
      saved.Add rebuild
      CancellableTaskResult.singleton ()

let private run (dependencies : FakeDependencies) =
  (RebuildSite.withDependencies dependencies () CancellationToken.None).Result

let private queued repo =
  Ok (Queued (RunNumber (if repo = production then 41L else 7L)))

let private rebuild dispatch = { DispatchedDate = utcNow; Production = dispatch }

[<Tests>]
let tests =
  testList "RebuildSite" [
    test "Rebuild site dispatches to the two repos and keeps the production run number" {
      let dependencies = FakeDependencies queued

      Expect.equal
        (run dependencies)
        (Ok (RebuildSite.Rebuilt (Queued (RunNumber 41L))))
        "Expected the production run"
      Expect.equal dependencies.Dispatched [ production; staging ] "Expected a dispatch to each repo"
      Expect.equal dependencies.Saved [ rebuild (Dispatched (Queued (RunNumber 41L))) ] "Expected the saved rebuild"
    }

    test "a failed dispatch returns an error with the repo, and the other repo still gets its dispatch" {
      let dependencies = FakeDependencies(fun repo -> if repo = staging then Error refused else queued repo)

      Expect.equal
        (run dependencies)
        (Error (ActionError.Logic (RebuildSite.DispatchFailed [ staging, refused ])))
        "Expected the failed staging dispatch"
      Expect.equal dependencies.Dispatched [ production; staging ] "Expected a dispatch to each repo"
      Expect.equal dependencies.Saved [ rebuild (Dispatched (Queued (RunNumber 41L))) ] "Expected the production run"
    }

    test "a failed production dispatch saves a rebuild with no run number" {
      let dependencies = FakeDependencies(fun repo -> if repo = production then Error refused else queued repo)

      Expect.equal
        (run dependencies)
        (Error (ActionError.Logic (RebuildSite.DispatchFailed [ production, refused ])))
        "Expected the failed production dispatch"
      Expect.equal dependencies.Saved [ rebuild Failed ] "Expected no run number"
    }

    test "a logged dispatch is a rebuild with no run" {
      let dependencies = FakeDependencies(fun _ -> Ok Logged)

      Expect.equal (run dependencies) (Ok (RebuildSite.Rebuilt Logged)) "Expected the log"
      Expect.equal dependencies.Saved [ rebuild (Dispatched Logged) ] "Expected the saved log"
    }

    test "a queued production run with no run number is a rebuild" {
      let dependencies =
        FakeDependencies(fun repo -> if repo = production then Ok QueuedWithoutRunNumber else queued repo)

      Expect.equal (run dependencies) (Ok (RebuildSite.Rebuilt QueuedWithoutRunNumber)) "Expected the rebuild"
      Expect.equal dependencies.Saved [ rebuild (Dispatched QueuedWithoutRunNumber) ] "Expected no run number"
    }
  ]
