[<RequireQualifiedAccess>]
module TwoPoint.Admin.Typed

type State =
  | Closed
  | Typing of typed: string
  | Sending of typed: string
  | Failed of typed: string * reason: string

let isOpen (state: State) = state <> Closed

let isSending (state: State) =
  match state with
  | Sending _ -> true
  | _ -> false

let typed (state: State) =
  match state with
  | Closed -> ""
  | Typing typed
  | Sending typed
  | Failed(typed, _) -> typed

let reason (state: State) =
  match state with
  | Failed(_, reason) -> Some reason
  | _ -> None
