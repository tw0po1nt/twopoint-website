[<RequireQualifiedAccess>]
module TwoPoint.Admin.Publish

open System.Text.RegularExpressions

type Schedule =
  | Now
  | OnDate of typed: string

type UpdateDate =
  | KeepDate
  | UpdateOn of typed: string

type When =
  | Draft of Schedule
  | Changes of UpdateDate

type State =
  | Closed
  | Saving
  | Choosing of When
  | Checking of When
  | Checked of When * Api.Problem list
  | CheckFailed of When * reason: string
  | Sending of When
  | Failed of When * reason: string

type Model =
  { State: State
    Warnings: Api.PublishWarning list }

let init: Model = { State = Closed; Warnings = [] }

let withState (state: State) (model: Model) = { model with State = state }

let closedWith (warnings: Api.PublishWarning list) : Model = { State = Closed; Warnings = warnings }

let dismissed (model: Model) = { model with Warnings = [] }

let isOpen (model: Model) =
  match model.State with
  | Closed -> false
  | Saving
  | Choosing _
  | Checking _
  | Checked _
  | CheckFailed _
  | Sending _
  | Failed _ -> true

let private complete = Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2})?$")

let isoOf (typed: string) =
  if complete.IsMatch typed then
    Some(if typed.Length = 16 then $"{typed}:00Z" else $"{typed}Z")
  else
    None

let target (choice: When) : Api.PublishTarget option =
  match choice with
  | Draft Now -> Some(Api.Publish Api.PublishNow)
  | Draft(OnDate typed) -> isoOf typed |> Option.map (Api.PublishOn >> Api.Publish)
  | Changes KeepDate -> Some(Api.PublishChanges Api.KeepUpdateDate)
  | Changes(UpdateOn typed) -> isoOf typed |> Option.map (Api.SetUpdateDate >> Api.PublishChanges)

let isChanges (choice: When) =
  match choice with
  | Draft _ -> false
  | Changes _ -> true

let hasErrors (problems: Api.Problem list) =
  problems |> List.exists (fun problem -> problem.Severity = Api.Severity.Error)

let choice (model: Model) =
  match model.State with
  | Closed
  | Saving -> None
  | Choosing choice
  | Checking choice
  | Checked(choice, _)
  | CheckFailed(choice, _)
  | Sending choice
  | Failed(choice, _) -> Some choice

let isSending (model: Model) =
  match model.State with
  | Sending _ -> true
  | _ -> false

let canConfirm (model: Model) =
  match model.State with
  | Checked(_, problems) -> not (hasErrors problems)
  | _ -> false

let problems (model: Model) =
  match model.State with
  | Checked(_, problems) -> problems
  | _ -> []

let reason (model: Model) =
  match model.State with
  | CheckFailed(_, reason)
  | Failed(_, reason) -> Some reason
  | _ -> None

let statusText (model: Model) =
  let changes = choice model |> Option.exists isChanges
  match model.State with
  | Closed -> ""
  | Saving -> "Saving the post…"
  | Choosing _ -> "Enter a date and time."
  | Checking _ -> "Checking the post…"
  | Checked(_, problems) when hasErrors problems -> "Fix the errors below to publish."
  | Checked(_, []) -> "No problems found."
  | Checked _ -> "Warnings found. You can still publish."
  | CheckFailed _ -> "Couldn't check the post."
  | Sending _ -> if changes then "Publishing…" else "Publishing…"
  | Failed _ -> if changes then "Couldn't publish the changes." else "Couldn't publish the post."

let warningText (warning: Api.PublishWarning) =
  match warning with
  | Api.CommentRowMissing -> "Comments aren't open on this post yet (its post-table row is missing)."
  | Api.PendingEditKept -> "The post is live, but a leftover pending edit wasn't cleared. Use 'Discard changes' to remove it."
  | Api.NewerTextKept -> "Edits saved in another tab during the publish are kept as a pending edit."
  | Api.ProductionRebuildFailed -> "Couldn't start the production rebuild. Click 'Rebuild site' on the dashboard."
  | Api.StagingRebuildFailed -> "Couldn't start the staging rebuild. Click 'Rebuild site' on the dashboard."
  | Api.SiteNotRebuilt -> "Couldn't start the rebuild. Click 'Rebuild site' on the dashboard."
  | Api.DeployNotTracked -> "Can't track this post's deploy status. The site is still rebuilding."

let changeWarningText (warning: Api.ChangeWarning) =
  match warning with
  | Api.PublishStep Api.NewerTextKept -> "Edits saved in another tab during the change are kept as a pending edit."
  | Api.PublishStep Api.CommentRowMissing ->
    "Comments still follow the old publish date (its post-table row wasn't updated)."
  | Api.PublishStep step -> warningText step
  | Api.PendingDateNotUpdated -> "The pending edit still has the old publish date. 'Publish changes' will correct it."
  | Api.PostRowKept ->
    "Comments are still open on this post (its post-table row wasn't deleted)."
  | Api.RevisionsKept -> "The post's revisions weren't deleted, but no one can see them."
  | Api.CommentsKept -> "The post's comments weren't deleted, but no one can see them."
