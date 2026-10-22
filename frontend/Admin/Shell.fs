[<RequireQualifiedAccess>]
module TwoPoint.Admin.Shell

/// Each API call of the admin. A call needs a token.
type Call = LoadDashboard

type Model =
  { Router: Router.Model
    Session: Session.Model<Call>
    Dashboard: Dashboard.Model }

type Msg =
  | Router of Router.Msg
  | Session of Session.Msg<Call>
  | Dashboard of Dashboard.Msg

type Effect =
  | Navigate of Router.Effect
  | SignIn of Session.Effect<Call>
  | Send of Call

let init (path: string) : Model * Effect option =
  let session, effect = Session.init ()

  { Router = Router.init path
    Session = session
    Dashboard = Dashboard.init },
  Option.map SignIn effect

let rec update (msg: Msg) (model: Model) : Model * Effect option =
  match msg with
  | Router msg ->
    let router, effect = Router.update msg model.Router
    { model with Router = router }, Option.map Navigate effect
  | Session msg ->
    let session, effect = Session.update msg model.Session
    let next = { model with Session = session }

    if model.Session.Status = Session.Starting && Session.isActive session then
      next, Some(Send LoadDashboard)
    else
      next, Option.map SignIn effect
  | Dashboard msg ->
    let next = { model with Dashboard = Dashboard.update msg model.Dashboard }

    match msg with
    | Dashboard.Answered(Error Api.Refused) -> update (Session Session.AccountRefused) next
    | Dashboard.Answered _ -> next, None
