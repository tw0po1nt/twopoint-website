[<RequireQualifiedAccess>]
module TwoPoint.Admin.SiteDeploy

type Status =
  | Waiting
  | Loaded of Api.SiteDeploy
  | NotLoaded of reason: string

type Model =
  { Status: Status
    /// Counts each load and each restart of the reads. A timer and a poll answer name the generation that they belong to.
    Generation: int }

type Msg =
  | Answered of Result<Api.SiteDeploy, Api.Failure>
  | Polled of generation: int * Result<Api.SiteDeploy, Api.Failure>
  | PollElapsed of generation: int

type Effect =
  | Poll of generation: int
  /// Starts a timer of `Deploy.pollDelay` milliseconds. The timer sends `PollElapsed`.
  | WaitToPoll of generation: int

let init: Model = { Status = Waiting; Generation = 0 }

let reload (model: Model) : Model =
  { Status = Waiting
    Generation = model.Generation + 1 }

let restart (model: Model) : Model =
  { model with Generation = model.Generation + 1 }

let private isDeploying (deploy: Api.SiteDeploy) = deploy.Deploy = Api.DeployState.Deploying

let private waitIfDeploying (generation: int) (deploy: Api.SiteDeploy) =
  if isDeploying deploy then Some(WaitToPoll generation) else None

let update (msg: Msg) (model: Model) : Model * Effect option =
  match msg with
  | Answered(Ok deploy) -> { model with Status = Loaded deploy }, waitIfDeploying model.Generation deploy
  | Answered(Error failure) -> { model with Status = NotLoaded(Api.reason failure) }, None
  | Polled(generation, _) when generation <> model.Generation -> model, None
  | Polled(generation, Ok deploy) -> { model with Status = Loaded deploy }, waitIfDeploying generation deploy
  | Polled(generation, Error Api.Unanswered) -> model, Some(WaitToPoll generation)
  | Polled(_, Error _) -> model, None
  | PollElapsed generation when generation <> model.Generation -> model, None
  | PollElapsed generation ->
    match model.Status with
    | Loaded deploy when isDeploying deploy -> model, Some(Poll generation)
    | Waiting
    | Loaded _
    | NotLoaded _ -> model, None

let private failedRun (run: Api.SiteRun option) =
  run |> Option.filter (fun run -> run.State = Api.RunState.Failed)

let notDeployedTitle = "Latest change isn't live."

let notDeployedText =
  "The rebuild didn't start, or GitHub canceled the run. Click 'Rebuild site' to try again."

let private ofLoaded (read: Api.SiteDeploy -> 'T option) (model: Model) =
  match model.Status with
  | Loaded deploy -> read deploy
  | Waiting
  | NotLoaded _ -> None

let state (model: Model) =
  model |> ofLoaded (fun deploy -> Some deploy.Deploy)

let productionFailure (model: Model) =
  model |> ofLoaded (fun deploy -> failedRun deploy.Production)

/// The rebuild did not start, or GitHub canceled the run, so the site did not deploy and no production run failed.
let notDeployed (model: Model) =
  model
  |> ofLoaded (fun deploy -> Some(deploy.Deploy = Api.DeployState.DeployFailed && (failedRun deploy.Production).IsNone))
  |> Option.defaultValue false

let stagingWarning (model: Model) =
  model |> ofLoaded (fun deploy -> failedRun deploy.Staging)

let reason (model: Model) =
  match model.Status with
  | NotLoaded reason -> Some reason
  | Waiting
  | Loaded _ -> None

let productionFailureTitle (run: Api.SiteRun) = $"Production run #{run.Number} failed."

let productionFailureText =
  "The live site didn't update. Open the run in GitHub to see the error, then click 'Rebuild site'."

let stagingWarningTitle (run: Api.SiteRun) = $"Staging run #{run.Number} failed."

let stagingWarningText =
  "Unreleased code in staging couldn't build the published posts. Production isn't affected."
