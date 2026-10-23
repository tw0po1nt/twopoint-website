[<RequireQualifiedAccess>]
module TwoPoint.Admin.Shell

/// Each API call of the admin. A call needs a token.
type Call = LoadDashboard

/// A sheet or a dialog over the page. One at a time is open.
type Overlay =
  | MoreSheet
  | SignOutDialog

type Model =
  { Router: Router.Model
    Session: Session.Model<Call>
    Dashboard: Dashboard.Model
    Overlay: Overlay option }

type Msg =
  | Router of Router.Msg
  | Session of Session.Msg<Call>
  | Dashboard of Dashboard.Msg
  | Opened of Overlay
  /// The browser closed the overlay, for example after the Esc key.
  | Closed of Overlay

type Effect =
  | Navigate of Router.Effect
  | SignIn of Session.Effect<Call>
  | Send of Call

let init (path: string) : Model * Effect option =
  let session, effect = Session.init ()

  { Router = Router.init path
    Session = session
    Dashboard = Dashboard.init
    Overlay = None },
  Option.map SignIn effect

let rec update (msg: Msg) (model: Model) : Model * Effect option =
  match msg with
  | Router msg ->
    let router, effect = Router.update msg model.Router

    { model with
        Router = router
        Overlay = None },
    Option.map Navigate effect
  | Session msg ->
    let session, effect = Session.update msg model.Session

    let next =
      { model with
          Session = session
          Overlay = if msg = Session.SignOutClicked then None else model.Overlay }

    if model.Session.Status = Session.Starting && Session.isActive session then
      next, Some(Send LoadDashboard)
    else
      next, Option.map SignIn effect
  | Dashboard msg ->
    let next = { model with Dashboard = Dashboard.update msg model.Dashboard }

    match msg with
    | Dashboard.Answered(Error Api.Refused) -> update (Session Session.AccountRefused) next
    | Dashboard.Answered _ -> next, None
  | Opened overlay -> { model with Overlay = Some overlay }, None
  | Closed overlay when model.Overlay = Some overlay -> { model with Overlay = None }, None
  | Closed _ -> model, None
