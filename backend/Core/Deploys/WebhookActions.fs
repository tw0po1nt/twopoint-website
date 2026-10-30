namespace TwoPoint.Core.Deploys.Actions

open TwoPoint.Core.Deploys
open TwoPoint.Core.Notifications
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

open System
open System.Security.Cryptography
open System.Text.Json
open System.Text.Json.Serialization

// ====================================================================================================================
// Receive a workflow run
// ====================================================================================================================

[<RequireQualifiedAccess>]
module ReceiveWorkflowRun =

  /// `Signature` is the value of `X-Hub-Signature-256`, and `Event` is the value of `X-GitHub-Event`.
  type Dto =
    { Event : string
      Signature : string option
      Body : byte array }

  type Malformed =
    | NoWorkflowRun
    | MissingField of name : string
    | NoRepoName
    | BadJson of exn

  type Error =
    | BadSignature
    | MalformedPayload of Malformed

  type Skipped =
    | NotWorkflowRunEvent
    | NotSiteWorkflow
    | UnknownRepo
    | UnknownRunEvent

  type Notification =
    | NoNotification
    | Notified of Pushed
    | DevicesNotRead of DependencyError

  type Event =
    | Recorded of SiteRun * Notification
    | Skipped of Skipped
    | OlderThanStored

  type Next =
    | Newer of run : SiteRun * before : RunState option
    | OlderThanStored

  type Parsed =
    | Run of SiteRun
    | Skip of Skipped

  type SignatureCheck =
    | SignatureValid
    | SignatureInvalid

  [<CLIMutable>]
  type RepositoryJson =
    { [<JsonPropertyName "full_name">]
      FullName : string }

  [<CLIMutable>]
  type WorkflowRunJson =
    { [<JsonPropertyName "run_number">]
      RunNumber : int64
      [<JsonPropertyName "run_attempt">]
      RunAttempt : int
      Event : string
      Status : string
      Conclusion : string
      [<JsonPropertyName "created_at">]
      CreatedAt : DateTimeOffset
      [<JsonPropertyName "updated_at">]
      UpdatedAt : DateTimeOffset
      [<JsonPropertyName "html_url">]
      HtmlUrl : string
      Path : string }

  [<CLIMutable>]
  type PayloadJson =
    { [<JsonPropertyName "workflow_run">]
      WorkflowRun : WorkflowRunJson
      Repository : RepositoryJson }

  type DeployFailedPayload = { Run : RunNumber }

  [<RequireQualifiedAccess>]
  module DeployFailedPayload =

    /// The worker `sw.js` builds the notification of a failed deploy from a payload of this case.
    [<Literal>]
    let Case = "deploy-failed"

    let ofRun (run : SiteRun) : DeployFailedPayload = { Run = run.Number }

    let toBytes (payload : DeployFailedPayload) : byte array =
      JsonSerializer.SerializeToUtf8Bytes {| ``case`` = Case; runNumber = payload.Run.Value |}

  [<RequireQualifiedAccess>]
  module Logic =

    let private prefix = "sha256="

    let checkSignature (secret : byte array) (body : byte array) (header : string option) : SignatureCheck =
      match header with
      | Some header when header.StartsWith(prefix, StringComparison.Ordinal) ->
        try
          let sent = Convert.FromHexString(header.Substring prefix.Length)
          let expected = HMACSHA256.HashData(secret, body)

          if CryptographicOperations.FixedTimeEquals(sent, expected) then
            SignatureValid
          else
            SignatureInvalid
        with :? FormatException ->
          SignatureInvalid
      | _ -> SignatureInvalid

    /// GitHub writes the conclusion of a canceled run with the British spelling.
    let state (status : string) (conclusion : string) =
      match status, conclusion with
      | "completed", "success" -> RunState.Succeeded
      | "completed", "skipped" -> RunState.Canceled
      | "completed", conclusion when conclusion.StartsWith "cancel" -> RunState.Canceled
      | "completed", _ -> RunState.Failed
      | "in_progress", _ -> RunState.Running
      | _ -> RunState.Waiting

    let private options = JsonSerializerOptions(PropertyNameCaseInsensitive = true)

    let private siteWorkflowPath = ".github/workflows/site.yml"

    let private run (repo : Repo) (url : Uri) (json : WorkflowRunJson) =
      match RunEvent.tryParse json.Event with
      | Some event ->
        Run
          { Repo = repo
            Number = RunNumber json.RunNumber
            Attempt = RunAttempt json.RunAttempt
            Event = event
            State = state json.Status (json.Conclusion |> Option.ofObj |> Option.defaultValue "")
            CreatedDate = json.CreatedAt.UtcDateTime
            UpdatedDate = json.UpdatedAt.UtcDateTime
            Url = url }
      | None -> Skip UnknownRunEvent

    let parse (repos : SiteRepos) (event : string) (body : byte array) : Result<Parsed, Malformed> =
      if event <> "workflow_run" then
        Ok (Skip NotWorkflowRunEvent)
      else
        try
          let payload = JsonSerializer.Deserialize<PayloadJson>(body, options)

          if isNull (box payload) || isNull (box payload.WorkflowRun) || isNull (box payload.Repository) then
            Error NoWorkflowRun
          else
            let json = payload.WorkflowRun

            if isNull json.Path then
              Error (MissingField "path")
            elif isNull json.Event then
              Error (MissingField "event")
            elif isNull json.Status then
              Error (MissingField "status")
            elif json.Path.Split('@')[0] <> siteWorkflowPath then
              Ok (Skip NotSiteWorkflow)
            else
              match Repo.tryParse payload.Repository.FullName with
              | false, _ -> Error NoRepoName
              | true, repo when repo <> repos.Production && repo <> repos.Staging -> Ok (Skip UnknownRepo)
              | true, repo ->
                match Uri.TryCreate(json.HtmlUrl, UriKind.Absolute) with
                | true, url -> Ok (run repo url json)
                | false, _ -> Error (MissingField "html_url")
        with :? JsonException as ex ->
          Error (BadJson ex)

    let private rank (state : RunState) =
      match state with
      | RunState.Waiting -> 0
      | RunState.Running -> 1
      | RunState.Succeeded
      | RunState.Failed
      | RunState.Canceled -> 2

    /// GitHub sends the webhooks of a run in no fixed order, so a stored run never goes back to an earlier state or
    /// an earlier attempt. A later attempt starts again from no state.
    let next (stored : SiteRun option) (incoming : SiteRun) : Next =
      match stored with
      | Some stored when stored.Attempt.Value > incoming.Attempt.Value -> Next.OlderThanStored
      | Some stored when stored.Attempt.Value < incoming.Attempt.Value -> Newer(incoming, None)
      | Some stored when rank stored.State > rank incoming.State -> Next.OlderThanStored
      | Some stored when rank stored.State = rank incoming.State && stored.UpdatedDate > incoming.UpdatedDate ->
        Next.OlderThanStored
      | _ -> Newer(incoming, stored |> Option.map _.State)

    /// GitHub can send the webhook of a finished run more than one time.
    let needsNotification (repos : SiteRepos) (run : SiteRun) (before : RunState option) =
      run.Repo = repos.Production && run.State = RunState.Failed && before <> Some RunState.Failed

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetSiteRepos: unit -> SiteRepos
    abstract member GetWebhookSecret: unit -> DependencyResult<byte array>
    abstract member SaveRun: run: SiteRun -> DependencyResult<Next>
    abstract member GetDevicesOfAuthor: unit -> DependencyResult<RegisteredDevice list>
    abstract member SendPush: RegisteredDevice -> payload: byte array -> DependencyResult<PushOutcome>
    abstract member RecordPush: RegisteredDevice -> SiteRun -> DependencyResult<unit>
    abstract member DeleteDevice: RegisteredDevice -> DependencyResult<unit>

  let withDependencies (dependencies : Dependencies) : Action =

    let notify (run : SiteRun) : CancellableTask<Notification> = cancellableTask {
      match! dependencies.GetDevicesOfAuthor() with
      | Error error -> return DevicesNotRead error
      | Ok devices ->
        let payload = DeployFailedPayload.ofRun run |> DeployFailedPayload.toBytes

        let! pushed =
          devices
          |> DevicePush.pushAll
            (fun device -> dependencies.SendPush device payload)
            (fun device -> dependencies.RecordPush device run)
            dependencies.DeleteDevice

        return Notified pushed
    }

    fun dto -> cancellableTaskResult {
      let! secret = dependencies.GetWebhookSecret() |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      match Logic.checkSignature secret dto.Body dto.Signature with
      | SignatureInvalid -> return! Result.Error (ActionError.Logic BadSignature)
      | SignatureValid ->
        match Logic.parse (dependencies.GetSiteRepos()) dto.Event dto.Body with
        | Error malformed -> return! Result.Error (ActionError.Logic (MalformedPayload malformed))
        | Ok (Skip skipped) -> return Skipped skipped
        | Ok (Run run) ->
          match! dependencies.SaveRun run |> CancellableTaskResult.mapError ActionError<Error>.Dependency with
          | Newer(saved, before) when Logic.needsNotification (dependencies.GetSiteRepos()) saved before ->
            let! notification = notify saved |> CancellableTask.map Ok
            return Recorded(saved, notification)
          | Newer(saved, _) -> return Recorded(saved, NoNotification)
          | Next.OlderThanStored -> return Event.OlderThanStored
    }
