[<RequireQualifiedAccess>]
module TwoPoint.Admin.Deploy

/// The time between two reads of the deploy state while a change deploys, in milliseconds.
let pollDelay = 10000

let badgeText (state: Api.DeployState) =
  match state with
  | Api.DeployState.Deploying -> "Deploying"
  | Api.DeployState.Deployed -> "Deployed"
  | Api.DeployState.DeployFailed -> "Deploy failed"

type Status =
  | Unread
  | Known of Api.DeployState
  | Unreadable of reason: string

type Retry =
  | NotRetried
  | Retrying
  | RetryFailed of reason: string

type Model = { Status: Status; Retry: Retry }

type Msg =
  | StatusRead of Result<Api.DeployState, Api.Failure>
  | PollElapsed
  | RetryClicked
  | Retried of Result<Api.Rebuilt, Api.Failure>

type Effect =
  | ReadStatus
  /// Starts a timer of `pollDelay` milliseconds. The timer sends `PollElapsed`.
  | WaitToPoll
  /// Dispatches the rebuild of the site.
  | Rebuild

let init: Model = { Status = Unread; Retry = NotRetried }

let update (msg: Msg) (model: Model) : Model * Effect option =
  match msg, model.Status with
  | StatusRead(Ok Api.DeployState.Deploying), _ -> { model with Status = Known Api.DeployState.Deploying }, Some WaitToPoll
  | StatusRead(Ok state), _ -> { model with Status = Known state }, None
  | StatusRead(Error Api.Unanswered), _ ->
    { model with Status = Unreadable(Api.reason Api.Unanswered) }, Some WaitToPoll
  | StatusRead(Error failure), _ -> { model with Status = Unreadable(Api.reason failure) }, None
  | PollElapsed, (Unread | Unreadable _ | Known Api.DeployState.Deploying) -> model, Some ReadStatus
  | PollElapsed, Known(Api.DeployState.Deployed | Api.DeployState.DeployFailed) -> model, None
  | RetryClicked, Known Api.DeployState.DeployFailed when model.Retry <> Retrying -> { model with Retry = Retrying }, Some Rebuild
  | RetryClicked, _ -> model, None
  | Retried(Ok _), _ -> { Status = Unread; Retry = NotRetried }, Some ReadStatus
  | Retried(Error failure), _ -> { model with Retry = RetryFailed(Api.reason failure) }, None

let statusText (model: Model) =
  match model.Status with
  | Unread -> None
  | Known Api.DeployState.Deploying -> Some "deploying…"
  | Known Api.DeployState.Deployed -> Some "deployed"
  | Known Api.DeployState.DeployFailed -> Some "deploy failed"
  | Unreadable _ -> Some "deploy state unknown"

let canRetry (model: Model) =
  model.Status = Known Api.DeployState.DeployFailed && model.Retry <> Retrying

let retryFailure (model: Model) =
  match model.Retry with
  | RetryFailed reason -> reason
  | NotRetried
  | Retrying -> ""
