namespace TwoPoint.Core.Deploys.Actions

open TwoPoint.Core.Deploys
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

  type Error =
    | BadSignature
    | MalformedPayload of reason : string

  type Skipped =
    | NotWorkflowRunEvent
    | NotSiteWorkflow
    | UnknownRepo
    | UnknownRunEvent
    | OlderThanStored

  type Event =
    | Recorded of SiteRun
    | Skipped of Skipped

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
      Event : string
      Status : string
      Conclusion : string
      [<JsonPropertyName "created_at">]
      CreatedAt : DateTimeOffset
      [<JsonPropertyName "updated_at">]
      UpdatedAt : DateTimeOffset
      Path : string }

  [<CLIMutable>]
  type PayloadJson =
    { [<JsonPropertyName "workflow_run">]
      WorkflowRun : WorkflowRunJson
      Repository : RepositoryJson }

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

    let private run (repo : Repo) (json : WorkflowRunJson) =
      match json.Event with
      | "push"
      | "workflow_dispatch" ->
        Run
          { Repo = repo
            Number = RunNumber json.RunNumber
            Event = if json.Event = "push" then Push else WorkflowDispatch
            State = state json.Status (json.Conclusion |> Option.ofObj |> Option.defaultValue "")
            CreatedDate = json.CreatedAt.UtcDateTime
            UpdatedDate = json.UpdatedAt.UtcDateTime }
      | _ -> Skip UnknownRunEvent

    let parse (repos : SiteRepos) (event : string) (body : byte array) : Result<Parsed, string> =
      if event <> "workflow_run" then
        Ok (Skip NotWorkflowRunEvent)
      else
        try
          let payload = JsonSerializer.Deserialize<PayloadJson>(body, options)

          if isNull (box payload) || isNull (box payload.WorkflowRun) || isNull (box payload.Repository) then
            Error "The payload has no workflow_run or no repository"
          else
            let json = payload.WorkflowRun

            if isNull json.Path || isNull json.Event || isNull json.Status then
              Error "The workflow_run has no path, event, or status"
            elif json.Path.Split('@')[0] <> siteWorkflowPath then
              Ok (Skip NotSiteWorkflow)
            else
              match Repo.tryParse payload.Repository.FullName with
              | false, _ -> Error "The repository has no full name"
              | true, repo when repo = repos.Production || repo = repos.Staging -> Ok (run repo json)
              | true, _ -> Ok (Skip UnknownRepo)
        with :? JsonException as ex ->
          Error ex.Message

    let private rank (state : RunState) =
      match state with
      | RunState.Waiting -> 0
      | RunState.Running -> 1
      | RunState.Succeeded
      | RunState.Failed
      | RunState.Canceled -> 2

    /// GitHub sends the webhooks of a run in no fixed order, so a stored run never goes back to an earlier state.
    let next (stored : SiteRun option) (incoming : SiteRun) : SiteRun option =
      match stored with
      | Some stored when rank stored.State > rank incoming.State -> None
      | Some stored when rank stored.State = rank incoming.State && stored.UpdatedDate > incoming.UpdatedDate -> None
      | _ -> Some incoming

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetSiteRepos: unit -> SiteRepos
    abstract member GetWebhookSecret: unit -> DependencyResult<byte array>
    abstract member GetRun: repo: Repo -> number: RunNumber -> DependencyResult<SiteRun option>
    abstract member SaveRun: run: SiteRun -> DependencyResult<unit>

  let withDependencies (dependencies : Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! secret = dependencies.GetWebhookSecret() |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      match Logic.checkSignature secret dto.Body dto.Signature with
      | SignatureInvalid -> return! Result.Error (ActionError.Logic BadSignature)
      | SignatureValid ->
        match Logic.parse (dependencies.GetSiteRepos()) dto.Event dto.Body with
        | Error reason -> return! Result.Error (ActionError.Logic (MalformedPayload reason))
        | Ok (Skip skipped) -> return Skipped skipped
        | Ok (Run run) ->
          let! stored =
            dependencies.GetRun run.Repo run.Number |> CancellableTaskResult.mapError ActionError<Error>.Dependency

          match Logic.next stored run with
          | None -> return Skipped OlderThanStored
          | Some next ->
            do! dependencies.SaveRun next |> CancellableTaskResult.mapError ActionError<Error>.Dependency
            return Recorded next
    }
