namespace TwoPoint.Core.Deploys

open TwoPoint.Core.Posts
open TwoPoint.Core.Util

open System

/// A GitHub repo that builds the site with `site.yml`.
type Repo =
  { Owner : string
    Name : string }

  override this.ToString() = $"{this.Owner}/{this.Name}"

module Repo =
  let tryParse (value : string) =
    match value.Split '/' with
    | [| owner; name |] when owner <> "" && name <> "" -> true, { Owner = owner; Name = name }
    | _ -> false, { Owner = ""; Name = "" }

  let workflowPage (repo : Repo) = Uri $"https://github.com/{repo.Owner}/{repo.Name}/actions/workflows/site.yml"

/// The production repo deploys the site of record. The staging repo deploys the same posts with the unreleased code.
type SiteRepos =
  { Production : Repo
    Staging : Repo }

/// GitHub numbers the runs of each workflow in a repo from 1.
type RunNumber =
  | RunNumber of int64

  member this.Value =
    let (RunNumber number) = this
    number

/// A re-run keeps the run number. GitHub numbers the attempts of each run from 1.
type RunAttempt =
  | RunAttempt of int

  member this.Value =
    let (RunAttempt attempt) = this
    attempt

type Dispatch =
  | Queued of RunNumber
  | QueuedWithoutRunNumber
  /// The local backend logs each dispatch and sends nothing.
  | Logged

module Dispatch =
  let name (dispatch : Dispatch) =
    match dispatch with
    | Queued _
    | QueuedWithoutRunNumber -> "queued"
    | Logged -> "logged"

  let runNumber (dispatch : Dispatch) =
    match dispatch with
    | Queued run -> Some run
    | QueuedWithoutRunNumber
    | Logged -> None

type ProductionDispatch =
  | Dispatched of Dispatch
  /// The rebuild has no run number. A run that GitHub queues after the rebuild deploys it.
  | Failed

module ProductionDispatch =
  let name (production : ProductionDispatch) =
    match production with
    | Dispatched dispatch -> Dispatch.name dispatch
    | Failed -> "failed"

  let runNumber (production : ProductionDispatch) =
    match production with
    | Dispatched dispatch -> Dispatch.runNumber dispatch
    | Failed -> None

  let tryParse (name : string) (number : int64 option) =
    match name, number with
    | "queued", Some number -> Some (Dispatched (Queued (RunNumber number)))
    | "queued", None -> Some (Dispatched QueuedWithoutRunNumber)
    | "logged", _ -> Some (Dispatched Logged)
    | "failed", _ -> Some Failed
    | _ -> None

type Rebuild =
  { DispatchedDate : DateTime
    Production : ProductionDispatch }

type SiteDispatch =
  { Production : Result<Dispatch, DependencyError>
    Staging : Result<Dispatch, DependencyError> }

module SiteDispatch =
  let production (dispatched : Result<SiteDispatch, DependencyError>) =
    match dispatched with
    | Ok site ->
      match site.Production with
      | Ok dispatch -> ProductionDispatch.Dispatched dispatch
      | Error _ -> ProductionDispatch.Failed
    | Error _ -> ProductionDispatch.Failed

type RunEvent =
  | Push
  | WorkflowDispatch

module RunEvent =
  let name (event : RunEvent) =
    match event with
    | Push -> "push"
    | WorkflowDispatch -> "workflow_dispatch"

  let tryParse (name : string) =
    match name with
    | "push" -> Some Push
    | "workflow_dispatch" -> Some WorkflowDispatch
    | _ -> None

[<RequireQualifiedAccess>]
type RunState =
  | Waiting
  | Running
  | Succeeded
  | Failed
  | Canceled

type SiteRun =
  { Repo : Repo
    Number : RunNumber
    Attempt : RunAttempt
    Event : RunEvent
    State : RunState
    CreatedDate : DateTime
    UpdatedDate : DateTime
    Url : Uri }

type ProductionRuns =
  { Latest : SiteRun option
    LatestSucceeded : SiteRun option }

type Change =
  { ChangedDate : DateTime
    Production : ProductionDispatch }

module Change =
  let ofRebuild (rebuild : Rebuild) : Change =
    { ChangedDate = rebuild.DispatchedDate; Production = rebuild.Production }

type PostChange =
  { Slug : Slug
    Change : Change }

module PostChange =
  let ofDispatch (slug : Slug) (changedDate : DateTime) (dispatched : Result<SiteDispatch, DependencyError>) =
    { Slug = slug
      Change = { ChangedDate = changedDate; Production = SiteDispatch.production dispatched } }

type PostChanges =
  abstract member RecordChange: change: PostChange -> DependencyResult<unit>

type DeployContext =
  { Changes : Map<Slug, Change>
    Rebuild : Rebuild option
    Runs : ProductionRuns }
