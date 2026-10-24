namespace TwoPoint.Core.Deploys

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

type Rebuild =
  { DispatchedDate : DateTime
    Production : ProductionDispatch }
