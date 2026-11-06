[<RequireQualifiedAccess>]
module TwoPoint.Admin.Confirm

type State =
  | Closed
  | Confirming
  | Sending
  | Failed of reason: string

let isOpen (state: State) = state <> Closed

let isSending (state: State) = state = Sending

let reason (state: State) =
  match state with
  | Failed reason -> Some reason
  | _ -> None
