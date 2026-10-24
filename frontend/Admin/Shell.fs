[<RequireQualifiedAccess>]
module TwoPoint.Admin.Shell

/// Each API call of the admin. A call needs a token.
type Call =
  | LoadDashboard
  | LoadPosts
  | LoadQueue
  | TrustCommenter of comment: Api.PostedComment
  | Moderate of comment: Api.PostedComment * Queue.Action
  | LoadPostComments of slug: string
  | ModeratePostComment of slug: string * comment: Api.Comment * Queue.Action
  | LoadCommenters
  | LoadCommenter of commenterId: string
  | ChangeCommenter of commenterId: string * Commenter.Action
  | LoadSubscriptions
  | LoadSubscriptionDeliveries of subscriptionId: string
  | CancelSubscription of subscriptionId: string
  | DeleteSubscription of subscriptionId: string
  | LoadDevices
  /// Reads the subscription of the browser with no prompt, and sends it.
  | SyncDevice
  | RegisterDevice of PushKeys
  | RemoveDevice of deviceId: string
  | RebuildSite

/// A sheet or a dialog over the page. One at a time is open.
type Overlay =
  | MoreSheet
  | SignOutDialog
  | DeleteDialog of commentId: string
  | DeleteCommenterDialog
  | DeleteSubscriptionDialog

type Model =
  { Router: Router.Model
    Session: Session.Model<Call>
    Dashboard: Dashboard.Model
    Rebuild: Rebuild.Model
    Posts: Posts.Model
    Queue: Queue.Model
    PostComments: PostComments.Model option
    Commenters: Commenters.Model
    Commenter: Commenter.Model option
    Subscriptions: Subscriptions.Model
    Notifications: Notifications.Model
    Overlay: Overlay option }

type Msg =
  | Router of Router.Msg
  | Session of Session.Msg<Call>
  | Dashboard of Dashboard.Msg
  | Rebuild of Rebuild.Msg
  | Posts of Posts.Msg
  | Queue of Queue.Msg
  | PostComments of PostComments.Msg
  | Commenters of Commenters.Msg
  | Commenter of Commenter.Msg
  | Subscriptions of Subscriptions.Msg
  | Notifications of Notifications.Msg
  | Opened of Overlay
  /// The browser closed the overlay, for example after the Esc key.
  | Closed of Overlay

type Effect =
  /// The calls load the page that the author goes to.
  | Navigate of Router.Effect * calls: Call list
  | SignIn of Session.Effect<Call>
  | Send of calls: Call list
  /// Asks the browser for the permission and the subscription. A click starts it, so no token call comes first.
  | Subscribe

let private enter (route: Route) (model: Model) =
  let left =
    { model with
        PostComments = None
        Commenter = None }

  match route with
  | Route.Posts -> { left with Posts = Posts.reload model.Posts }, [ LoadPosts ]
  | Route.PostComments slug -> { left with PostComments = Some(PostComments.init slug) }, [ LoadPostComments slug ]
  | Route.Commenters -> { left with Commenters = Commenters.reload model.Commenters }, [ LoadCommenters ]
  | Route.Commenter commenterId -> { left with Commenter = Some(Commenter.init commenterId) }, [ LoadCommenter commenterId ]
  | Route.Subscriptions ->
    { left with Subscriptions = Subscriptions.reload model.Subscriptions }, [ LoadSubscriptions ]
  | Route.Settings -> { left with Notifications = Notifications.reload model.Notifications }, [ LoadDevices ]
  | Route.Dashboard
  | Route.Post _
  | Route.ModerationQueue
  | Route.Series
  | Route.NotFound _ -> left, []

let init (path: string) : Model * Effect option =
  let session, effect = Session.init ()
  let router = Router.init path

  { Router = router
    Session = session
    Dashboard = Dashboard.init
    Rebuild = Rebuild.init
    Posts = Posts.init
    Queue = Queue.init
    PostComments = None
    Commenters = Commenters.init
    Commenter = None
    Subscriptions = Subscriptions.init
    Notifications = Notifications.init
    Overlay = None }
  |> enter router.Route
  |> fst,
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

    let page, pageCalls =
      if router.Route <> model.Router.Route then
        enter router.Route model
      else
        model, []

    let calls =
      if model.Session.Status <> Session.Starting then
        pageCalls
      else
        []

    let next =
      { page with
          Router = router
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
      let sync = if model.Notifications.Registration = Notifications.Registering then [ SyncDevice ] else []
      next, Some(Send([ LoadDashboard; LoadQueue ] @ snd (enter model.Router.Route model) @ sync))
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
  | Posts msg ->
    let next = { model with Posts = Posts.update msg model.Posts }

    match msg with
    | Posts.Answered(Error Api.Refused) -> update (Session Session.AccountRefused) next
    | Posts.Answered _
    | Posts.Filtered _ -> next, None
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
  | Commenters msg ->
    let next = { model with Commenters = Commenters.update msg model.Commenters }

    match msg with
    | Commenters.Answered(Error Api.Refused) -> update (Session Session.AccountRefused) next
    | Commenters.Answered _
    | Commenters.Filtered _ -> next, None
  | Commenter(Commenter.Chosen Commenter.Delete) when model.Overlay <> Some DeleteCommenterDialog -> model, None
  | Commenter msg ->
    let page, effect =
      match model.Commenter with
      | Some page ->
        let page, effect = Commenter.update msg page
        Some page, effect
      | None -> None, None

    let next =
      { model with
          Commenter = page
          Overlay =
            match msg with
            | Commenter.Chosen Commenter.Delete -> None
            | _ -> model.Overlay }

    match msg with
    | Commenter.Answered(_, Error Api.Refused)
    | Commenter.Done(_, _, Error Api.Refused) -> update (Session Session.AccountRefused) next
    | Commenter.Done(commenterId, Commenter.Delete, Ok()) when next.Router.Route = Route.Commenter commenterId ->
      match update (Router(Router.LinkFollowed(Route.path Route.Commenters))) next with
      | next, Some(Navigate(push, calls)) -> next, Some(Navigate(push, calls @ [ LoadQueue ]))
      | next, _ -> next, Some(Send [ LoadQueue ])
    // A change of the commenter can change the comments in the moderation queue.
    | Commenter.Done(_, _, Ok()) -> next, Some(Send [ LoadQueue ])
    | Commenter.Answered _
    | Commenter.Chosen _
    | Commenter.Done(_, _, Error _) ->
      next,
      effect
      |> Option.map (fun (Commenter.Change(commenterId, action)) -> Send [ ChangeCommenter(commenterId, action) ])
  | Subscriptions(Subscriptions.Chosen(_, Subscriptions.Delete)) when model.Overlay <> Some DeleteSubscriptionDialog ->
    model, None
  | Subscriptions msg ->
    let subscriptions, effect = Subscriptions.update msg model.Subscriptions

    let next =
      { model with
          Subscriptions = subscriptions
          Overlay =
            match msg with
            | Subscriptions.Chosen(_, Subscriptions.Delete) -> None
            | _ -> model.Overlay }

    match msg with
    | Subscriptions.Answered(Error Api.Refused)
    | Subscriptions.DeliveriesAnswered(_, Error Api.Refused)
    | Subscriptions.Canceled(_, Error Api.Refused)
    | Subscriptions.Deleted(_, Error Api.Refused) -> update (Session Session.AccountRefused) next
    | _ ->
      next,
      effect
      |> Option.map (fun effect ->
        match effect with
        | Subscriptions.LoadDeliveries subscriptionId -> Send [ LoadSubscriptionDeliveries subscriptionId ]
        | Subscriptions.CancelSubscription subscriptionId -> Send [ CancelSubscription subscriptionId ]
        | Subscriptions.DeleteSubscription subscriptionId -> Send [ DeleteSubscription subscriptionId ])
  | Notifications msg ->
    let notifications, effect = Notifications.update msg model.Notifications
    let next = { model with Notifications = notifications }

    match msg with
    | Notifications.DevicesAnswered(Error Api.Refused)
    | Notifications.RegisterAnswered(Error Api.Refused)
    | Notifications.Removed(_, Error Api.Refused) -> update (Session Session.AccountRefused) next
    // A start with the permission granted sends the subscription when the session is active.
    | Notifications.Detected _ when notifications.Registration = Notifications.Registering && Session.isActive model.Session ->
      next, Some(Send [ SyncDevice ])
    | Notifications.RegisterAnswered(Ok(Some _)) when model.Router.Route = Route.Settings -> next, Some(Send [ LoadDevices ])
    | _ ->
      next,
      effect
      |> Option.map (fun effect ->
        match effect with
        | Notifications.Subscribe -> Subscribe
        | Notifications.Register keys -> Send [ RegisterDevice keys ]
        | Notifications.Sync -> Send [ SyncDevice ]
        | Notifications.Remove deviceId -> Send [ RemoveDevice deviceId ])
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
  | Some DeleteCommenterDialog, _
  | Some DeleteSubscriptionDialog, _
  | Some MoreSheet, _
  | Some SignOutDialog, _
  | None, _ -> None

let deleteCommenterChosen (model: Model) =
  match model.Overlay, model.Router.Route with
  | Some DeleteCommenterDialog, Route.Commenter _ ->
    model.Commenter
    |> Option.bind Commenter.tryCommenter
    |> Option.map (fun _ -> Commenter(Commenter.Chosen Commenter.Delete))
  | Some DeleteCommenterDialog, _
  | Some(DeleteDialog _), _
  | Some DeleteSubscriptionDialog, _
  | Some MoreSheet, _
  | Some SignOutDialog, _
  | None, _ -> None

let deleteSubscriptionChosen (model: Model) =
  match model.Overlay, model.Router.Route, model.Subscriptions.Sheet with
  | Some DeleteSubscriptionDialog, Route.Subscriptions, Some sheet ->
    Subscriptions.tryOpened model.Subscriptions
    |> Option.map (fun _ -> Subscriptions(Subscriptions.Chosen(sheet.Id, Subscriptions.Delete)))
  | Some DeleteSubscriptionDialog, _, _
  | Some DeleteCommenterDialog, _, _
  | Some(DeleteDialog _), _, _
  | Some MoreSheet, _, _
  | Some SignOutDialog, _, _
  | None, _, _ -> None
