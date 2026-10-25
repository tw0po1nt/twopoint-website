module TwoPoint.Core.Tests.ReceiveWorkflowRunTests

open TwoPoint.Core.Deploys
open TwoPoint.Core.Deploys.Actions
open TwoPoint.Core.Util

open Expecto
open FsToolkit.ErrorHandling
open IcedTasks

open System
open System.Security.Cryptography
open System.Text
open System.Threading

let private production = { Owner = "tw0po1nt"; Name = "twopoint-website" }
let private staging = { Owner = "tw0po1nt"; Name = "twopoint-website-staging" }
let private secret = Encoding.UTF8.GetBytes "a-webhook-secret"

type private FakeStore(stored : SiteRun list) =
  let saved = ResizeArray<SiteRun>()
  let mutable secretFails = false

  member _.Saved = List.ofSeq saved
  member _.SecretFails with set value = secretFails <- value

  interface ReceiveWorkflowRun.Dependencies with
    member _.GetSiteRepos () = { Production = production; Staging = staging }

    member _.GetWebhookSecret () =
      if secretFails then
        CancellableTaskResult.error { Dependency = Dependency.KeyVault; Type = Unknown (exn "Key Vault is down") }
      else
        CancellableTaskResult.singleton secret

    member _.SaveRun run =
      let current = stored |> List.tryFind (fun item -> item.Repo = run.Repo && item.Number = run.Number)

      match ReceiveWorkflowRun.Logic.next current run with
      | ReceiveWorkflowRun.Newer run ->
        saved.Add run
        CancellableTaskResult.singleton (ReceiveWorkflowRun.Newer run)
      | ReceiveWorkflowRun.Next.OlderThanStored -> CancellableTaskResult.singleton ReceiveWorkflowRun.Next.OlderThanStored

let private payload repo number event status (conclusion : string) path =
  let conclusion = if conclusion = "" then "null" else $"\"{conclusion}\""

  $"""{{ "action": "completed",
  "workflow_run": {{ "run_number": {number}, "event": "{event}", "status": "{status}", "conclusion": {conclusion},
    "created_at": "2026-03-01T09:00:00Z", "updated_at": "2026-03-01T09:05:00Z", "path": "{path}" }},
  "repository": {{ "full_name": "{repo}" }} }}"""
  |> Encoding.UTF8.GetBytes

let private siteRun repo number status conclusion =
  payload repo number "workflow_dispatch" status conclusion ".github/workflows/site.yml"

let private sign (body : byte array) = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(secret, body)).ToLowerInvariant()

let private receive (store : FakeStore) event signature body =
  (ReceiveWorkflowRun.withDependencies store { Event = event; Signature = signature; Body = body } CancellationToken.None).Result

let private receiveSigned store body = receive store "workflow_run" (Some (sign body)) body

[<Tests>]
let tests =
  testList "Receive a workflow run" [
    test "a webhook with a bad signature is refused and writes nothing" {
      let store = FakeStore []
      let body = siteRun "tw0po1nt/twopoint-website" 5 "completed" "success"
      let wrong = "sha256=" + String('0', 64)

      Expect.equal (receive store "workflow_run" (Some wrong) body) (Error(ActionError.Logic ReceiveWorkflowRun.BadSignature)) "Refused"
      Expect.isEmpty store.Saved "No run is written"
    }

    test "a webhook with no signature is refused and writes nothing" {
      let store = FakeStore []
      let body = siteRun "tw0po1nt/twopoint-website" 5 "completed" "success"

      Expect.equal (receive store "workflow_run" None body) (Error(ActionError.Logic ReceiveWorkflowRun.BadSignature)) "Refused"
      Expect.isEmpty store.Saved "No run is written"
    }

    test "a signature that is not hexadecimal is refused" {
      let store = FakeStore []
      let body = siteRun "tw0po1nt/twopoint-website" 5 "completed" "success"

      Expect.equal (receive store "workflow_run" (Some "sha256=zz") body) (Error(ActionError.Logic ReceiveWorkflowRun.BadSignature)) "Refused"
      Expect.equal (receive store "workflow_run" (Some "sha1=00") body) (Error(ActionError.Logic ReceiveWorkflowRun.BadSignature)) "Refused"
      Expect.isEmpty store.Saved "No run is written"
    }

    test "a signature of another body is refused" {
      let store = FakeStore []
      let body = siteRun "tw0po1nt/twopoint-website" 5 "completed" "success"
      let other = siteRun "tw0po1nt/twopoint-website" 6 "completed" "success"

      Expect.equal (receive store "workflow_run" (Some (sign other)) body) (Error(ActionError.Logic ReceiveWorkflowRun.BadSignature)) "Refused"
      Expect.isEmpty store.Saved "No run is written"
    }

    test "a webhook that is signed records the repo, the run number, the event, the state, and the dates" {
      let store = FakeStore []
      let body = siteRun "tw0po1nt/twopoint-website" 5 "completed" "success"

      let expected =
        { Repo = production
          Number = RunNumber 5L
          Event = WorkflowDispatch
          State = RunState.Succeeded
          CreatedDate = DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc)
          UpdatedDate = DateTime(2026, 3, 1, 9, 5, 0, DateTimeKind.Utc) }

      Expect.equal (receiveSigned store body) (Ok(ReceiveWorkflowRun.Recorded expected)) "Recorded"
      Expect.equal store.Saved [ expected ] "One run is written"
    }

    test "a run of the staging repo is recorded" {
      let store = FakeStore []
      receiveSigned store (siteRun "tw0po1nt/twopoint-website-staging" 8 "in_progress" "") |> ignore

      Expect.equal (store.Saved |> List.map (fun run -> run.Repo, run.State)) [ staging, RunState.Running ] "A running run"
    }

    test "a push run is recorded as a push" {
      let store = FakeStore []
      receiveSigned store (payload "tw0po1nt/twopoint-website" 9 "push" "queued" "" ".github/workflows/site.yml") |> ignore

      Expect.equal (store.Saved |> List.map (fun run -> run.Event, run.State)) [ Push, RunState.Waiting ] "A waiting push"
    }

    test "a path with a ref after the file name is the site workflow" {
      let store = FakeStore []
      receiveSigned store (payload "tw0po1nt/twopoint-website" 9 "push" "queued" "" ".github/workflows/site.yml@refs/heads/main") |> ignore

      Expect.hasLength store.Saved 1 "The run is written"
    }

    test "a run of another workflow is ignored" {
      let store = FakeStore []
      let body = payload "tw0po1nt/twopoint-website" 9 "push" "completed" "success" ".github/workflows/pr-text.yml"

      Expect.equal (receiveSigned store body) (Ok(ReceiveWorkflowRun.Skipped ReceiveWorkflowRun.NotSiteWorkflow)) "Ignored"
      Expect.isEmpty store.Saved "No run is written"
    }

    test "a run of another repo is ignored" {
      let store = FakeStore []

      Expect.equal
        (receiveSigned store (siteRun "someone/else" 9 "completed" "success"))
        (Ok(ReceiveWorkflowRun.Skipped ReceiveWorkflowRun.UnknownRepo))
        "Ignored"

      Expect.isEmpty store.Saved "No run is written"
    }

    test "an event that is not workflow_run is ignored" {
      let store = FakeStore []
      let body = Encoding.UTF8.GetBytes "{}"

      Expect.equal
        (receive store "ping" (Some (sign body)) body)
        (Ok(ReceiveWorkflowRun.Skipped ReceiveWorkflowRun.NotWorkflowRunEvent))
        "Ignored"

      Expect.isEmpty store.Saved "No run is written"
    }

    test "a payload that is signed and not readable is malformed" {
      let store = FakeStore []
      let body = Encoding.UTF8.GetBytes "not json"

      match receiveSigned store body with
      | Error(ActionError.Logic(ReceiveWorkflowRun.MalformedPayload(ReceiveWorkflowRun.BadJson _))) -> ()
      | other -> failtestf "Expected a malformed payload, got %A" other

      Expect.isEmpty store.Saved "No run is written"
    }

    test "a late webhook of an earlier state does not replace a finished run" {
      let finished =
        { Repo = production
          Number = RunNumber 5L
          Event = WorkflowDispatch
          State = RunState.Succeeded
          CreatedDate = DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc)
          UpdatedDate = DateTime(2026, 3, 1, 9, 5, 0, DateTimeKind.Utc) }

      let store = FakeStore [ finished ]

      Expect.equal
        (receiveSigned store (siteRun "tw0po1nt/twopoint-website" 5 "in_progress" ""))
        (Ok ReceiveWorkflowRun.Event.OlderThanStored)
        "Ignored"

      Expect.isEmpty store.Saved "The stored run stays"
    }

    test "a secret that Key Vault does not give is a dependency error and writes nothing" {
      let store = FakeStore []
      store.SecretFails <- true
      let body = siteRun "tw0po1nt/twopoint-website" 5 "completed" "success"

      match receiveSigned store body with
      | Error(ActionError.Dependency _) -> ()
      | other -> failtestf "Expected a dependency error, got %A" other

      Expect.isEmpty store.Saved "No run is written"
    }

    test "a payload with no workflow_run, no path, or no repository name is malformed with the named case" {
      let store = FakeStore []
      let sign' body = receiveSigned store (Encoding.UTF8.GetBytes(body: string))

      Expect.equal (sign' "{}") (Error(ActionError.Logic(ReceiveWorkflowRun.MalformedPayload ReceiveWorkflowRun.NoWorkflowRun))) "No run"

      Expect.equal
        (sign' "{ \"workflow_run\": { \"run_number\": 1, \"event\": \"push\", \"status\": \"queued\" }, \"repository\": { \"full_name\": \"a/b\" } }")
        (Error(ActionError.Logic(ReceiveWorkflowRun.MalformedPayload(ReceiveWorkflowRun.MissingField "path"))))
        "No path"

      Expect.equal
        (sign' "{ \"workflow_run\": { \"run_number\": 1, \"event\": \"push\", \"status\": \"queued\", \"path\": \".github/workflows/site.yml\" }, \"repository\": { \"full_name\": \"nothing\" } }")
        (Error(ActionError.Logic(ReceiveWorkflowRun.MalformedPayload ReceiveWorkflowRun.NoRepoName)))
        "No repo name"
    }

    test "a run with a lower state or an older update never replaces the stored run" {
      let at minutes = DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc).AddMinutes(float minutes)

      let run state updated =
        { Repo = production
          Number = RunNumber 5L
          Event = Push
          State = state
          CreatedDate = at 0
          UpdatedDate = at updated }

      Expect.equal (ReceiveWorkflowRun.Logic.next None (run RunState.Waiting 0)) (ReceiveWorkflowRun.Newer(run RunState.Waiting 0)) "No stored run"
      Expect.equal (ReceiveWorkflowRun.Logic.next (Some(run RunState.Running 1)) (run RunState.Succeeded 2)) (ReceiveWorkflowRun.Newer(run RunState.Succeeded 2)) "A later state"
      Expect.equal (ReceiveWorkflowRun.Logic.next (Some(run RunState.Succeeded 2)) (run RunState.Running 3)) ReceiveWorkflowRun.Next.OlderThanStored "An earlier state"
      Expect.equal (ReceiveWorkflowRun.Logic.next (Some(run RunState.Failed 5)) (run RunState.Failed 4)) ReceiveWorkflowRun.Next.OlderThanStored "An older update"
    }

    test "a conclusion maps to the state of the run" {
      Expect.equal (ReceiveWorkflowRun.Logic.state "completed" "success") RunState.Succeeded "success"
      Expect.equal (ReceiveWorkflowRun.Logic.state "completed" "failure") RunState.Failed "failure"
      Expect.equal (ReceiveWorkflowRun.Logic.state "completed" "timed_out") RunState.Failed "timed_out"
      Expect.equal (ReceiveWorkflowRun.Logic.state "completed" "skipped") RunState.Canceled "skipped"
      Expect.equal (ReceiveWorkflowRun.Logic.state "in_progress" "") RunState.Running "in_progress"
      Expect.equal (ReceiveWorkflowRun.Logic.state "queued" "") RunState.Waiting "queued"
    }
  ]
