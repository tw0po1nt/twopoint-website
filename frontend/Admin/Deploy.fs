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

/// A timer names the chain of reads that started it. Each editor page and each restart of the reads has its own chain.
type Chain = { Page: int; Number: int }

type Model =
  { Status: Status
    Retry: Progress<unit>
    Chain: Chain }

type Msg =
  | StatusRead of Result<Api.DeployState, Api.Failure>
  | PollElapsed of Chain
  | RetryClicked
  | Retried of Result<Api.Rebuilt, Api.Failure>

type Effect =
  | ReadStatus
  /// Starts a timer of `pollDelay` milliseconds. The timer sends `PollElapsed`.
  | WaitToPoll of Chain
  | Rebuild

let init (page: int) : Model =
  { Status = Unread
    Retry = Progress.Ready
    Chain = { Page = page; Number = 0 } }

let restart (model: Model) : Model =
  { init model.Chain.Page with
      Chain = { model.Chain with Number = model.Chain.Number + 1 } }

let canRetry (model: Model) =
  model.Status = Known Api.DeployState.DeployFailed && not (Progress.isSending model.Retry)

let update (msg: Msg) (model: Model) : Model * Effect option =
  match msg, model.Status with
  | StatusRead(Ok Api.DeployState.Deploying), _ ->
    { model with Status = Known Api.DeployState.Deploying }, Some(WaitToPoll model.Chain)
  | StatusRead(Ok state), _ -> { model with Status = Known state }, None
  | StatusRead(Error Api.Unanswered), _ ->
    { model with Status = Unreadable(Api.reason Api.Unanswered) }, Some(WaitToPoll model.Chain)
  | StatusRead(Error failure), _ -> { model with Status = Unreadable(Api.reason failure) }, None
  | PollElapsed chain, _ when chain <> model.Chain -> model, None
  | PollElapsed _, (Unread | Unreadable _ | Known Api.DeployState.Deploying) -> model, Some ReadStatus
  | PollElapsed _, Known(Api.DeployState.Deployed | Api.DeployState.DeployFailed) -> model, None
  | RetryClicked, _ when canRetry model -> { model with Retry = Progress.Sending() }, Some Rebuild
  | RetryClicked, _ -> model, None
  | Retried(Ok _), _ -> restart model, Some ReadStatus
  | Retried(Error failure), _ -> { model with Retry = Progress.NotDone((), Api.reason failure) }, None

let statusText (model: Model) =
  match model.Status with
  | Unread -> None
  | Known Api.DeployState.Deploying -> Some "deploying…"
  | Known Api.DeployState.Deployed -> Some "deployed"
  | Known Api.DeployState.DeployFailed -> Some "deploy failed"
  | Unreadable _ -> Some "deploy state unknown"

let retryFailure (model: Model) =
  match model.Retry with
  | Progress.NotDone(_, reason) -> Some reason
  | Progress.Ready
  | Progress.Sending _ -> None
