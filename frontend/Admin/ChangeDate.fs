[<RequireQualifiedAccess>]
module TwoPoint.Admin.ChangeDate

/// `typed` is the text of a `datetime-local` field, in UTC.
type State =
  | Closed
  | Choosing of typed: string
  | Sending of typed: string
  | Failed of typed: string * reason: string

/// The date field starts with the current publish date, for example `2026-04-01T13:00Z` gives `2026-04-01T13:00`.
let initial (publishDate: string option) =
  match publishDate with
  | Some date when date.Length >= 16 -> date.Substring(0, 16)
  | _ -> ""

let isOpen (state: State) = state <> Closed

let isSending (state: State) =
  match state with
  | Sending _ -> true
  | _ -> false

let typed (state: State) =
  match state with
  | Closed -> ""
  | Choosing typed
  | Sending typed
  | Failed(typed, _) -> typed

let iso (state: State) = Publish.isoOf (typed state)

let canConfirm (state: State) =
  match state with
  | Choosing _
  | Failed _ -> (iso state).IsSome
  | Closed
  | Sending _ -> false

let reason (state: State) =
  match state with
  | Failed(_, reason) -> Some reason
  | _ -> None

let statusText (state: State) =
  match state with
  | Closed -> ""
  | Choosing typed
  | Failed(typed, _) when (Publish.isoOf typed).IsNone -> "Type the date and the time."
  | Choosing _ -> "The new date can be in the past. The post then shows as published."
  | Sending _ -> "The date changes."
  | Failed _ -> "The date is not changed."
