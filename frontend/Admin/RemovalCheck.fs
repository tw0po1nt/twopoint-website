[<RequireQualifiedAccess>]
module TwoPoint.Admin.RemovalCheck

/// The removal check of a published post. The delete dialog and the unpublish dialog run it when they open, and again after a
/// failed removal.
type State =
  | NotChecked
  | Checking
  | Checked of Api.RemovalCheck
  | Failed of reason: string

let answered (result: Result<Api.RemovalCheck, Api.Failure>) (state: State) =
  match state, result with
  | Checking, Ok outcome -> Checked outcome
  | Checking, Error failure -> Failed(Api.reason failure)
  | (NotChecked | Checked _ | Failed _), _ -> state

/// The check allows the removal.
let allows (state: State) =
  match state with
  | Checked(Api.PlainRemoval | Api.PublishAndRemove _) -> true
  | Checked(Api.Blocked _)
  | NotChecked
  | Checking
  | Failed _ -> false

/// The pending edits that the removal publishes first.
let edits (state: State) =
  match state with
  | Checked(Api.PublishAndRemove edits) -> edits
  | Checked(Api.PlainRemoval | Api.Blocked _)
  | NotChecked
  | Checking
  | Failed _ -> []

let problems (state: State) =
  match state with
  | Checked(Api.Blocked problems) -> problems
  | Checked(Api.PlainRemoval | Api.PublishAndRemove _)
  | NotChecked
  | Checking
  | Failed _ -> []

let failure (state: State) =
  match state with
  | Failed reason -> Some reason
  | NotChecked
  | Checking
  | Checked _ -> None

/// `plain` is the text with no edit to publish. `verb` is the removal, for example `delete`.
let buttonText (plain: string) (verb: string) (state: State) =
  match edits state with
  | [] -> plain
  | [ _ ] -> $"Publish 1 edit and {verb}"
  | edits -> $"Publish {edits.Length} edits and {verb}"
