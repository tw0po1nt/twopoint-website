[<RequireQualifiedAccess>]
module TwoPoint.Admin.Session

type Status =
  /// The admin reads the session and any response from Entra. No API call goes out yet.
  | Starting
  /// The page leaves for the Entra sign-in.
  | Redirecting
  | Active
  /// Entra needs the author to sign in again. The banner shows.
  | Expired of popupFailed: bool
  /// The sign-in popup is open.
  | Renewing
  /// The start or the redirect did not complete.
  | Broken

/// `Waiting` contains each call that failed because Entra needs the author to sign in again.
type Model<'call> = { Status: Status; Waiting: 'call list }

type Msg<'call> =
  | Started of hasSession: bool
  | SignInFailed
  | SignInClicked
  | InteractionRequired of failedCall: 'call
  | SignInAgainClicked
  | Renewed
  | RenewFailed

type Effect<'call> =
  | Start
  | SignInByRedirect
  | SignInByPopup
  | Retry of calls: 'call list

let init () : Model<'call> * Effect<'call> option = { Status = Starting; Waiting = [] }, Some Start

let private waitFor (call: 'call) (model: Model<'call>) =
  if List.contains call model.Waiting then
    model.Waiting
  else
    model.Waiting @ [ call ]

let update (msg: Msg<'call>) (model: Model<'call>) : Model<'call> * Effect<'call> option =
  match model.Status, msg with
  | Starting, Started true -> { model with Status = Active }, None
  | Starting, Started false -> { model with Status = Redirecting }, Some SignInByRedirect
  | (Starting | Redirecting), SignInFailed -> { model with Status = Broken }, None
  | Broken, SignInClicked -> { model with Status = Redirecting }, Some SignInByRedirect
  | Active, InteractionRequired call ->
    { Status = Expired false
      Waiting = waitFor call model }, None
  | (Expired _ | Renewing), InteractionRequired call -> { model with Waiting = waitFor call model }, None
  | Expired _, SignInAgainClicked -> { model with Status = Renewing }, Some SignInByPopup
  | Renewing, Renewed -> { Status = Active; Waiting = [] }, Some(Retry model.Waiting)
  | Renewing, RenewFailed -> { model with Status = Expired true }, None
  | _ -> model, None

let isActive (model: Model<'call>) = model.Status = Active

let showsBanner (model: Model<'call>) =
  match model.Status with
  | Expired _
  | Renewing -> true
  | Starting
  | Redirecting
  | Active
  | Broken -> false

let bannerText (model: Model<'call>) =
  match model.Status with
  | Expired true -> "The sign-in did not complete. Try again."
  | Expired false
  | Renewing -> "Your session ended. Sign in again to continue."
  | Starting
  | Redirecting
  | Active
  | Broken -> ""
