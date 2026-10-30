[<RequireQualifiedAccess>]
module TwoPoint.Admin.Revisions

type State =
  | Closed
  | Loading
  | LoadFailed of reason: string
  | Listed of Api.Revision list
  | Restoring of Api.Revision list * name: string
  | RestoreFailed of Api.Revision list * reason: string

let isOpen (state: State) = state <> Closed

let isRestoring (state: State) =
  match state with
  | Restoring _ -> true
  | _ -> false

let revisions (state: State) =
  match state with
  | Listed revisions
  | Restoring(revisions, _)
  | RestoreFailed(revisions, _) -> revisions
  | Closed
  | Loading
  | LoadFailed _ -> []

let reason (state: State) =
  match state with
  | LoadFailed reason
  | RestoreFailed(_, reason) -> Some reason
  | _ -> None

let restoring (state: State) =
  match state with
  | Restoring(_, name) -> Some name
  | _ -> None

/// For example `2026-02-01T10:00:00Z` gives `10:00 UTC`.
let timeText (iso: string) =
  match iso.Split 'T' with
  | [| _; time |] when time.Length >= 5 -> $"{time.Substring(0, 5)} UTC"
  | _ -> ""
