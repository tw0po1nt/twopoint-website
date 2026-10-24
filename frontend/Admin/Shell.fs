[<RequireQualifiedAccess>]
module TwoPoint.Admin.Shell

/// Each API call of the admin. A call needs a token.
type Call =
  | LoadDashboard
  | LoadQueue
  | Moderate of comment: Api.QueuedComment * Queue.Action
  | RebuildSite

/// A sheet or a dialog over the page. One at a time is open.
type Overlay =
  | MoreSheet
  | SignOutDialog
  | DeleteDialog of commentId: string

type Model =
  { Router: Router.Model
    Session: Session.Model<Call>
    Dashboard: Dashboard.Model
    Rebuild: Rebuild.Model
    Queue: Queue.Model
    Overlay: Overlay option }

type Msg =
  | Router of Router.Msg
  | Session of Session.Msg<Call>
  | Dashboard of Dashboard.Msg
  | Rebuild of Rebuild.Msg
  | Queue of Queue.Msg
  | Opened of Overlay
  /// The browser closed the overlay, for example after the Esc key.
  | Closed of Overlay

type Effect =
  | Navigate of Router.Effect
  | SignIn of Session.Effect<Call>
  | Send of calls: Call list

let init (path: string) : Model * Effect option =
  let session, effect = Session.init ()

  { Router = Router.init path
    Session = session
    Dashboard = Dashboard.init
    Rebuild = Rebuild.init
    Queue = Queue.init
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
          Overlay =
            match session.Status with
            | Session.SigningOut _ -> None
            | _ -> model.Overlay }

    if model.Session.Status = Session.Starting && Session.isActive session then
      next, Some(Send [ LoadDashboard; LoadQueue ])
    else
      next, Option.map SignIn effect
  | Dashboard msg ->
    let next = { model with Dashboard = Dashboard.update msg model.Dashboard }

    match msg with
    | Dashboard.Answered(Error Api.Refused) -> update (Session Session.AccountRefused) next
    | Dashboard.Answered _ -> next, None
  | Rebuild msg ->
    let rebuild, effect = Rebuild.update msg model.Rebuild
    let next = { model with Rebuild = rebuild }

    match msg with
    | Rebuild.Answered(Error Api.Refused) -> update (Session Session.AccountRefused) next
    | _ -> next, effect |> Option.map (fun Rebuild.Dispatch -> Send [ RebuildSite ])
  | Queue(Queue.Chosen(commentId, Queue.Delete)) when model.Overlay <> Some(DeleteDialog commentId) -> model, None
  | Queue msg ->
    let queue, effect = Queue.update msg model.Queue

    let next =
      { model with
          Queue = queue
          Overlay =
            match msg with
            | Queue.Chosen(_, Queue.Delete) -> None
            | _ -> model.Overlay }

    match msg with
    | Queue.Answered(Error Api.Refused)
    | Queue.Done(_, _, Error Api.Refused) -> update (Session Session.AccountRefused) next
    | _ -> next, effect |> Option.map (fun (Queue.Moderate(comment, action)) -> Send [ Moderate(comment, action) ])
  | Opened overlay -> { model with Overlay = Some overlay }, None
  | Closed overlay when model.Overlay = Some overlay -> { model with Overlay = None }, None
  | Closed _ -> model, None
