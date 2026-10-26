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
  | Saving -> "The post saves first."
  | Choosing _ -> "Type the date and the time."
  | Checking _ -> "The check runs."
  | Checked(_, problems) when hasErrors problems -> "The post has errors. Fix them to publish."
  | Checked(_, []) -> "The check found no problem."
  | Checked _ -> "The check found warnings. A warning does not stop the publish."
  | CheckFailed _ -> "The check did not run."
  | Sending _ -> if changes then "The changes publish." else "The post publishes."
  | Failed _ -> if changes then "The changes are not published." else "The post is not published."

let warningText (warning: Api.PublishWarning) =
  match warning with
  | Api.CommentRowMissing -> "Readers cannot comment on this post yet. The post table has no row for it."
  | Api.PendingEditKept -> "The post has a pending edit. The published text is correct."
  | Api.NewerTextKept -> "Another tab saved newer text while the post published. That text is now a pending edit."
  | Api.ProductionRebuildFailed -> "The production rebuild did not start. Select Rebuild site on the dashboard."
  | Api.StagingRebuildFailed -> "The staging rebuild did not start. Select Rebuild site on the dashboard."
  | Api.SiteNotRebuilt -> "The site rebuild did not start. Select Rebuild site on the dashboard."
  | Api.DeployNotTracked -> "The admin cannot show the deploy state of this post. The site still builds."

let changeWarningText (warning: Api.ChangeWarning) =
  match warning with
  | Api.PublishStep Api.NewerTextKept -> "Another tab saved newer text during the change. That text is a pending edit."
  | Api.PublishStep Api.CommentRowMissing ->
    "The post table keeps the old date for this post. Readers can comment on the post only after that date."
  | Api.PublishStep step -> warningText step
  | Api.PendingDateNotUpdated -> "The pending edit keeps the old publish date. Publish changes sets the right date."
  | Api.PostRowKept ->
    "The post table still has a row for this post. Readers can comment on the post until the row is deleted."
  | Api.RevisionsKept -> "The revisions of the post are still in the post store. Nobody can see them."
  | Api.CommentsKept -> "The comments of the post are still in the comment table. Nobody can see them."
