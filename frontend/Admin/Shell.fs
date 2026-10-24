[<RequireQualifiedAccess>]
module TwoPoint.Admin.Shell

/// Each API call of the admin. A call needs a token.
type Call =
  | LoadDashboard
  | LoadQueue
  | TrustCommenter of comment: Api.QueuedComment
  | Moderate of comment: Api.QueuedComment * Queue.Action
  | LoadPostComments of slug: string
  | ModeratePostComment of slug: string * comment: Api.Comment * Queue.Action
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
    PostComments: PostComments.Model option
    Overlay: Overlay option }

type Msg =
  | Router of Router.Msg
  | Session of Session.Msg<Call>
  | Dashboard of Dashboard.Msg
  | Rebuild of Rebuild.Msg
  | Queue of Queue.Msg
  | PostComments of PostComments.Msg
  | Opened of Overlay
  /// The browser closed the overlay, for example after the Esc key.
  | Closed of Overlay

type Effect =
  /// The calls load the page that the author goes to.
  | Navigate of Router.Effect * calls: Call list
  | SignIn of Session.Effect<Call>
  | Send of calls: Call list

let private pageOf (route: Route) =
  match route with
  | Route.PostComments slug -> Some(PostComments.init slug), [ LoadPostComments slug ]
  | Route.Dashboard
  | Route.Posts
  | Route.Post _
  | Route.ModerationQueue
  | Route.Commenters
  | Route.Series
  | Route.Subscriptions
  | Route.Settings
  | Route.NotFound _ -> None, []

let init (path: string) : Model * Effect option =
  let session, effect = Session.init ()
  let router = Router.init path

  { Router = router
    Session = session
    Dashboard = Dashboard.init
    Rebuild = Rebuild.init
    Queue = Queue.init
    PostComments = fst (pageOf router.Route)
    Overlay = None },
  Option.map SignIn effect

let private syncQueue (msg: PostComments.Msg) (queue: Queue.Model) =
  match msg with
  | PostComments.Done(commentId, action, Ok()) -> fst (Queue.update (Queue.Done(commentId, action, Ok())) queue)
  | PostComments.Answered _
  | PostComments.Filtered _
  | PostComments.Chosen _
  | PostComments.Done(_, _, Error _) -> queue

let rec update (msg: Msg) (model: Model) : Model * Effect option =
  match msg with
  | Router msg ->
    let router, effect = Router.update msg model.Router
    let entered = router.Route <> model.Router.Route
    let postComments, pageCalls = pageOf router.Route

    let calls =
      if entered && model.Session.Status <> Session.Starting then
        pageCalls
      else
        []

    let next =
      { model with
          Router = router
          PostComments = if entered then postComments else model.PostComments
          Overlay = None }

    match effect, calls with
    | Some effect, calls -> next, Some(Navigate(effect, calls))
    | None, [] -> next, None
    | None, calls -> next, Some(Send calls)
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
      next, Some(Send([ LoadDashboard; LoadQueue ] @ snd (pageOf model.Router.Route)))
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
    | Queue.Trusted(_, Error Api.Refused)
    | Queue.Done(_, _, Error Api.Refused) -> update (Session Session.AccountRefused) next
    | _ ->
      next,
      effect
      |> Option.map (fun effect ->
        match effect with
        | Queue.Trust comment -> Send [ TrustCommenter comment ]
        | Queue.Moderate(comment, action) -> Send [ Moderate(comment, action) ])
  | PostComments(PostComments.Chosen(commentId, Queue.Delete)) when model.Overlay <> Some(DeleteDialog commentId) ->
    model, None
  | PostComments msg ->
    let page, effect =
      match model.PostComments with
      | Some page ->
        let page, effect = PostComments.update msg page
        Some page, effect
      | None -> None, None

    let next =
      { model with
          PostComments = page
          Queue = syncQueue msg model.Queue
          Overlay =
            match msg with
            | PostComments.Chosen(_, Queue.Delete) -> None
            | _ -> model.Overlay }

    match msg with
    | PostComments.Answered(_, Error Api.Refused)
    | PostComments.Done(_, _, Error Api.Refused) -> update (Session Session.AccountRefused) next
    | _ ->
      next,
      effect
      |> Option.map (fun (PostComments.Moderate(slug, comment, action)) -> Send [ ModeratePostComment(slug, comment, action) ])
  | Opened overlay -> { model with Overlay = Some overlay }, None
  | Closed overlay when model.Overlay = Some overlay -> { model with Overlay = None }, None
  | Closed _ -> model, None

/// None when that page has no such comment.
let deleteChosen (model: Model) =
  match model.Overlay, model.Router.Route with
  | Some(DeleteDialog commentId), Route.ModerationQueue ->
    Queue.tryFind commentId model.Queue
    |> Option.map (fun _ -> Queue(Queue.Chosen(commentId, Queue.Delete)))
  | Some(DeleteDialog commentId), Route.PostComments _ ->
    model.PostComments
    |> Option.bind (PostComments.tryFind commentId)
    |> Option.map (fun _ -> PostComments(PostComments.Chosen(commentId, Queue.Delete)))
  | Some(DeleteDialog _), _
  | Some MoreSheet, _
  | Some SignOutDialog, _
  | None, _ -> None
