[<RequireQualifiedAccess>]
module TwoPoint.Admin.Rebuild

type Model =
  | Idle
  | Sending
  | Started of Api.Rebuilt
  | NotStarted of reason: string

type Msg =
  | Clicked
  | Answered of Result<Api.Rebuilt, Api.Failure>

type Effect = Dispatch

let init: Model = Idle

let update (msg: Msg) (model: Model) : Model * Effect option =
  match model, msg with
  | Sending, Clicked -> model, None
  | (Idle | Started _ | NotStarted _), Clicked -> Sending, Some Dispatch
  | _, Answered(Ok rebuilt) -> Started rebuilt, None
  | _, Answered(Error failure) -> NotStarted(Api.reason failure), None

let isSending (model: Model) = model = Sending

let failed (model: Model) =
  match model with
  | NotStarted _ -> true
  | Idle
  | Sending
  | Started _ -> false

let reason (model: Model) =
  match model with
  | NotStarted reason -> reason
  | Idle
  | Sending
  | Started _ -> ""

let text (model: Model) =
  match model with
  | Idle -> "Rebuilds and redeploys production and staging."
  | Sending -> "Starting the rebuild…"
  | Started(Api.Queued run) -> $"Rebuild started (production run #{run})."
  | Started Api.QueuedWithoutRunNumber -> "Rebuild started. Check GitHub for details."
  | Started Api.Logged -> "Local mode: rebuild logged, nothing sent to GitHub."
  | NotStarted _ -> "Couldn't start the rebuild."
