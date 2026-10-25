[<RequireQualifiedAccess>]
module TwoPoint.Admin.Publish

open System.Text.RegularExpressions

type When =
  | Now
  /// The text of a datetime-local input, in UTC, for example `2026-11-02T13:00`.
  | OnDate of typed: string

type State =
  | Closed
  /// The dialog waits until the API has the text.
  | Saving
  /// The author chose a date, and the text is not a complete date yet.
  | Choosing of When
  | Checking of When
  | Checked of When * Api.Problem list
  | CheckFailed of When * reason: string
  | Sending of When
  | Failed of When * reason: string

type Model =
  { State: State
    /// The steps after the write of the published blob that failed.
    Notice: Api.PublishWarning list }

let init: Model = { State = Closed; Notice = [] }

let withState (state: State) (model: Model) = { model with State = state }

let closedWith (warnings: Api.PublishWarning list) : Model = { State = Closed; Notice = warnings }

let dismissed (model: Model) = { model with Notice = [] }

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

/// The time that the API takes, or `None` for a text that is not a complete date and time.
let isoOf (typed: string) =
  if complete.IsMatch typed then
    Some(if typed.Length = 16 then $"{typed}:00Z" else $"{typed}Z")
  else
    None

/// `None` is a choice that has no complete date. `Some None` is the publish time.
let publishDate (choice: When) =
  match choice with
  | Now -> Some None
  | OnDate typed -> isoOf typed |> Option.map Some

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

/// An error disables the publish. A warning does not.
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
  | Failed(_, reason) -> reason
  | _ -> ""

let statusText (model: Model) =
  match model.State with
  | Closed -> ""
  | Saving -> "The post saves first."
  | Choosing _ -> "Type the date and the time."
  | Checking _ -> "The check runs."
  | Checked(_, problems) when hasErrors problems -> "The post has errors. Fix them to publish."
  | Checked(_, []) -> "The check found no problem."
  | Checked _ -> "The check found warnings. A warning does not stop the publish."
  | CheckFailed _ -> "The check did not run."
  | Sending _ -> "The post publishes."
  | Failed _ -> "The post is not published."

let warningText (warning: Api.PublishWarning) =
  match warning with
  | Api.CommentRow ->
    "The row for comments did not save. Comments on the post fail until you publish a change to the post."
  | Api.PendingEdit -> "The draft text stays in the post store. The published post is correct."
  | Api.SiteRebuild -> "The site rebuild did not start. Select Rebuild site on the dashboard."
