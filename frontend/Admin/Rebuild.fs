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
  | Idle -> "Build and deploy the site again in the production repo and the staging repo."
  | Sending -> "The rebuild starts."
  | Started(Api.Queued run) -> $"The rebuild started. The production run is #{run}."
  | Started Api.QueuedWithoutRunNumber -> "The rebuild started. GitHub did not send the number of the production run."
  | Started Api.Logged -> "The local backend logged the rebuild and sent nothing to GitHub."
  | NotStarted _ -> "The rebuild did not start."
