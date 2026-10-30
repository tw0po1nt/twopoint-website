module TwoPoint.Core.Tests.ReceiveWorkflowRunTests

open TwoPoint.Core.Deploys
open TwoPoint.Core.Deploys.Actions
open TwoPoint.Core.Notifications
open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open Expecto
open FsToolkit.ErrorHandling
open IcedTasks

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Threading

let private production = { Owner = "tw0po1nt"; Name = "twopoint-website" }
let private staging = { Owner = "tw0po1nt"; Name = "twopoint-website-staging" }
let private secret = Encoding.UTF8.GetBytes "a-webhook-secret"

let private device (endpoint : string) =
  let at = DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc)
  let endpoint = PushEndpoint.create endpoint |> Fixtures.ok
  { RegisteredDevice.UserId = UserExternalId.create "6f1c1b5e-3d2a-4a43-9c6b-0d7a5b0e9a11" |> Fixtures.ok
    Id = DeviceId.ofEndpoint endpoint
    Endpoint = endpoint
    P256dh = NonEmptyString.create None "BNcRdre" |> Fixtures.ok
    Auth = NonEmptyString.create None "tBHItJI" |> Fixtures.ok
    Label = NonEmptyString.create None "Safari on macOS" |> Fixtures.ok
    CreatedDate = at
    LastSeenDate = at }

let private phone = device "https://push.example.com/send/phone"

type private FakeStore(stored : SiteRun list) =
  let saved = ResizeArray<SiteRun>()
  let pushes = ResizeArray<RegisteredDevice * byte array>()
  let recorded = ResizeArray<RegisteredDevice * SiteRun>()
  let mutable secretFails = false
  let mutable devicesFail = false

  member _.Saved = List.ofSeq saved
  member _.Pushes = List.ofSeq pushes
  member _.Recorded = List.ofSeq recorded
  member _.SecretFails with set value = secretFails <- value
  member _.DevicesFail with set value = devicesFail <- value

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
      | ReceiveWorkflowRun.Newer(run, before) ->
        saved.Add run
        CancellableTaskResult.singleton (ReceiveWorkflowRun.Newer(run, before))
      | ReceiveWorkflowRun.Next.OlderThanStored -> CancellableTaskResult.singleton ReceiveWorkflowRun.Next.OlderThanStored

    member _.GetDevicesOfAuthor () =
      if devicesFail then
        CancellableTaskResult.error { Dependency = Dependency.Database; Type = Unknown (exn "The table is down") }
      else
        CancellableTaskResult.singleton [ phone ]

    member _.SendPush device payload =
      pushes.Add(device, payload)
      CancellableTaskResult.singleton Delivered

    member _.RecordPush device run =
      recorded.Add(device, run)
      CancellableTaskResult.singleton ()

    member _.DeleteDevice _ = CancellableTaskResult.singleton ()

let private payload repo number event status (conclusion : string) path =
  let conclusion = if conclusion = "" then "null" else $"\"{conclusion}\""

  $"""{{ "action": "completed",
  "workflow_run": {{ "run_number": {number}, "event": "{event}", "status": "{status}", "conclusion": {conclusion},
    "created_at": "2026-03-01T09:00:00Z", "updated_at": "2026-03-01T09:05:00Z", "path": "{path}",
    "html_url": "https://github.com/{repo}/actions/runs/900{number}" }},
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
          UpdatedDate = DateTime(2026, 3, 1, 9, 5, 0, DateTimeKind.Utc)
          Url = Uri "https://github.com/tw0po1nt/twopoint-website/actions/runs/9005" }

      Expect.equal (receiveSigned store body) (Ok(ReceiveWorkflowRun.Recorded(expected, ReceiveWorkflowRun.NoNotice))) "Recorded"
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
          UpdatedDate = DateTime(2026, 3, 1, 9, 5, 0, DateTimeKind.Utc)
          Url = Uri "https://github.com/tw0po1nt/twopoint-website/actions/runs/9005" }

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
          UpdatedDate = at updated
          Url = Uri "https://github.com/tw0po1nt/twopoint-website/actions/runs/9005" }

      Expect.equal (ReceiveWorkflowRun.Logic.next None (run RunState.Waiting 0)) (ReceiveWorkflowRun.Newer(run RunState.Waiting 0, None)) "No stored run"
      Expect.equal (ReceiveWorkflowRun.Logic.next (Some(run RunState.Running 1)) (run RunState.Succeeded 2)) (ReceiveWorkflowRun.Newer(run RunState.Succeeded 2, Some RunState.Running)) "A later state"
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
  
    test "a failed production run sends one notification with the run number" {
      let store = FakeStore []
      let result = receiveSigned store (siteRun "tw0po1nt/twopoint-website" 5 "completed" "failure")

      match result with
      | Ok(ReceiveWorkflowRun.Recorded(run, ReceiveWorkflowRun.Notified pushed)) ->
        Expect.equal pushed.Sent [ phone ] "One push to the device"
        Expect.equal (store.Pushes |> List.map fst) [ phone ] "One notification"
        Expect.equal (store.Recorded |> List.map (fun (device, run) -> device, run.Number)) [ phone, RunNumber 5L ] "A push row"

        use json = JsonDocument.Parse(snd store.Pushes.Head)
        Expect.equal (json.RootElement.GetProperty("case").GetString()) "deploy-failed" "The case"
        Expect.equal (json.RootElement.GetProperty("runNumber").GetInt64()) 5L "The run number"
        Expect.equal run.State RunState.Failed "The run is recorded as failed"
      | other -> failtestf "Expected a notified failed run, got %A" other
    }

    test "a failed staging run sends no notification" {
      let store = FakeStore []
      let result = receiveSigned store (siteRun "tw0po1nt/twopoint-website-staging" 8 "completed" "failure")

      match result with
      | Ok(ReceiveWorkflowRun.Recorded(_, ReceiveWorkflowRun.NoNotice)) -> ()
      | other -> failtestf "Expected a recorded run with no notice, got %A" other

      Expect.isEmpty store.Pushes "No notification"
    }

    test "a successful production run sends no notification" {
      let store = FakeStore []
      receiveSigned store (siteRun "tw0po1nt/twopoint-website" 5 "completed" "success") |> ignore

      Expect.isEmpty store.Pushes "No notification"
    }

    test "a second webhook of the same failed production run sends no notification" {
      let at = DateTime(2026, 3, 1, 9, 5, 0, DateTimeKind.Utc)

      let failed =
        { Repo = production
          Number = RunNumber 5L
          Event = WorkflowDispatch
          State = RunState.Failed
          CreatedDate = at
          UpdatedDate = at
          Url = Uri "https://github.com/tw0po1nt/twopoint-website/actions/runs/9005" }

      let store = FakeStore [ failed ]
      receiveSigned store (siteRun "tw0po1nt/twopoint-website" 5 "completed" "failure") |> ignore

      Expect.isEmpty store.Pushes "No second notification"
    }

    test "a failed production run is recorded when the devices are not readable" {
      let store = FakeStore []
      store.DevicesFail <- true

      match receiveSigned store (siteRun "tw0po1nt/twopoint-website" 5 "completed" "failure") with
      | Ok(ReceiveWorkflowRun.Recorded(_, ReceiveWorkflowRun.DevicesNotRead _)) -> ()
      | other -> failtestf "Expected a recorded run with no read of the devices, got %A" other

      Expect.hasLength store.Saved 1 "The run is written"
    }

    test "a run with no link to its page is malformed" {
      let store = FakeStore []

      let body =
        "{ \"workflow_run\": { \"run_number\": 1, \"event\": \"push\", \"status\": \"queued\", \"path\": \".github/workflows/site.yml\" }, \"repository\": { \"full_name\": \"tw0po1nt/twopoint-website\" } }"
        |> Encoding.UTF8.GetBytes

      Expect.equal
        (receiveSigned store body)
        (Error(ActionError.Logic(ReceiveWorkflowRun.MalformedPayload(ReceiveWorkflowRun.MissingField "html_url"))))
        "No link"
    }
  ]
