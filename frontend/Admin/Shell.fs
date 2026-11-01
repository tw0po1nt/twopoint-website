[<RequireQualifiedAccess>]
module TwoPoint.Admin.Shell

/// Each API call of the admin. A call needs a token.
type Call =
  | LoadDashboard
  | LoadPosts
  | PollPosts of generation: int
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
  /// Like SyncDevice, and subscribes first when the browser has no subscription.
  | ResyncDevice
  | RegisterDevice of PushKeys
  | RemoveDevice of deviceId: string
  | RebuildSite
  | LoadSiteDeploy
  | PollSiteDeploy of generation: int
  | CreateDraft
  | LoadPost of slug: string
  | SavePost of Editor.SaveRequest
  | LoadDetailsOptions
  | UploadNewSeriesImage of file: Browser.Types.File
  | SaveNewSeries of SeriesForm.SaveRequest
  | ChangeSlug of Editor.SlugRequest
  | PreviewPost of Editor.PreviewRequest
  | CheckPublish of Editor.PublishRequest * choice: Publish.When
  | PublishPost of Editor.PublishRequest
  | LoadPostDeploy of slug: string
  | RetryPostDeploy of slug: string
  | DiscardPending of Editor.DiscardRequest
  | UnpublishPost of Editor.UnpublishRequest
  | ChangePostDate of Editor.ChangeDateRequest
  | DeletePost of Editor.DeleteRequest
  | LoadRevisions of slug: string
  | RestoreRevision of Editor.RestoreRequest
  | StartDelivery of Editor.DeliveryRequest
  | UploadAsset of id: int * file: Browser.Types.File
  | LoadSeries
  | SaveSeries of SeriesForm.SaveRequest
  | DeleteSeries of slug: string * eTag: string
  | UploadSeriesImage of file: Browser.Types.File

/// A sheet or a dialog over the page. One at a time is open.
type Overlay =
  | MoreSheet
  | SignOutDialog
  | DeleteDialog of commentId: string
  | DeleteCommenterDialog
  | DeleteSubscriptionDialog
  | DeleteSeriesDialog

type Model =
  { Router: Router.Model
    Session: Session.Model<Call>
    Dashboard: Dashboard.Model
    Rebuild: Rebuild.Model
    SiteDeploy: SiteDeploy.Model
    Posts: Posts.Model
    Editor: Editor.Model option
    Uploads: Upload.Model
    Queue: Queue.Model
    PostComments: PostComments.Model option
    Commenters: Commenters.Model
    Commenter: Commenter.Model option
    Subscriptions: Subscriptions.Model
    Series: Series.Model
    Notifications: Notifications.Model
    /// Counts each opened editor page. A deploy timer names the page that started it.
    Pages: int
    Overlay: Overlay option }

type Msg =
  | Router of Router.Msg
  | Session of Session.Msg<Call>
  | Dashboard of Dashboard.Msg
  | Rebuild of Rebuild.Msg
  | SiteDeploy of SiteDeploy.Msg
  | Posts of Posts.Msg
  | Editor of Editor.Msg
  | Upload of Upload.Msg
  | Queue of Queue.Msg
  | PostComments of PostComments.Msg
  | Commenters of Commenters.Msg
  | Commenter of Commenter.Msg
  | Subscriptions of Subscriptions.Msg
  | Series of Series.Msg
  | Notifications of Notifications.Msg
  | NewCommentAnnounced
  | OpenRequested of target: string
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
  | Unsubscribe
  | SetBadge of count: int
  /// Starts the autosave timer of the editor.
  | Autosave of version: int
  | WaitToPollDeploy of slug: string * Deploy.Chain
  | WaitToPollPosts of generation: int
  | WaitToPollSiteDeploy of generation: int
  | Focus of elementId: string
  | MoveToBodyLine of line: int

let private enter (route: Route) (model: Model) =
  let left =
    { model with
        Editor = None
        Uploads = Upload.init
        PostComments = None
        Commenter = None }

  match route with
  | Route.Posts _ -> { left with Posts = Posts.reload model.Posts }, [ LoadPosts ]
  | Route.Post slug ->
    { left with
        Editor = Some(Editor.init slug (model.Pages + 1))
        Pages = model.Pages + 1 },
    [ LoadPost slug ]
  | Route.PostComments slug ->
    { left with
        PostComments = Some(PostComments.init slug)
        Posts = Posts.reload model.Posts },
    [ LoadPostComments slug; LoadPosts ]
  | Route.Commenters -> { left with Commenters = Commenters.reload model.Commenters }, [ LoadCommenters ]
  | Route.Commenter commenterId -> { left with Commenter = Some(Commenter.init commenterId) }, [ LoadCommenter commenterId ]
  | Route.Subscriptions _ ->
    { left with Subscriptions = Subscriptions.reload model.Subscriptions }, [ LoadSubscriptions ]
  | Route.Settings -> { left with Notifications = Notifications.reload model.Notifications }, [ LoadDevices ]
  | Route.Dashboard ->
    { left with
        Dashboard = Dashboard.init
        SiteDeploy = SiteDeploy.reload model.SiteDeploy },
    [ LoadDashboard; LoadSiteDeploy ]
  | Route.Series ->
    { left with
        Series = Series.reload model.Series
        SiteDeploy = SiteDeploy.reload model.SiteDeploy },
    [ LoadSeries; LoadSiteDeploy ]
  | Route.ModerationQueue
  | Route.NotFound _ -> left, []

let init (path: string) : Model * Effect option =
  let session, effect = Session.init ()
  let router = Router.init path

  { Router = router
    Session = session
    Dashboard = Dashboard.init
    Rebuild = Rebuild.init
    SiteDeploy = SiteDeploy.init
    Posts = Posts.init
    Editor = None
    Uploads = Upload.init
    Queue = Queue.init
    PostComments = None
    Commenters = Commenters.init
    Commenter = None
    Subscriptions = Subscriptions.init
    Series = Series.init
    Notifications = Notifications.init
    Pages = 0
    Overlay = None }
  |> enter router.Route
  |> fst,
  Option.map SignIn effect

let private withBadge (before: Queue.Model) (after: Queue.Model) (model, effect) =
  match effect, Queue.count after with
  | None, Some count when Queue.count before <> Some count -> model, Some(SetBadge count)
  | _ -> model, effect

let private syncQueue (msg: PostComments.Msg) (queue: Queue.Model) =
  match msg with
  | PostComments.Done(commentId, action, Ok()) -> fst (Queue.update (Queue.Done(commentId, action, Ok())) queue)
  | PostComments.Answered _
  | PostComments.Filtered _
  | PostComments.Chosen _
  | PostComments.Done(_, _, Error _) -> queue

/// The browser sends the Esc key to the page and to the open dialog. Only the top layer must act on it.
let private layerOpenOverEditor (model: Model) =
  model.Overlay.IsSome
  || (match model.Editor with
      | Some(Editor.Loaded document) ->
        document.Details.Open
        || Publish.isOpen document.Publish
        || document.Changes = Editor.ChangesShown
        || Confirm.isOpen document.Discard
        || Confirm.isOpen document.Unpublish
        || Typed.isOpen document.ChangeDate
        || Editor.isDeleteOpen document
        || Revisions.isOpen document.Revisions
      | _ -> false)

/// The author goes to another page, and the editor has text that the API does not have.
let private leavesUnsavedText (model: Model) (path: string) =
  match model.Editor with
  | Some(Editor.Loaded document) -> (Router.init path).Route <> model.Router.Route && Editor.needsSave document
  | _ -> false

let rec update (msg: Msg) (model: Model) : Model * Effect option =
  match msg with
  | Router((Router.LinkFollowed path | Router.UrlChanged path) as routerMsg) when leavesUnsavedText model path ->
    match model.Editor with
    | Some editor ->
      let editor, effect = Editor.update (Editor.LeaveRequested path) editor
      let next = { model with Editor = Some editor }
      let calls =
        effect
        |> Option.toList
        |> List.collect (function
          | Editor.Save request -> [ SavePost request ]
          | Editor.Autosave _
          | Editor.LoadDetailsOptions
          | Editor.UploadSeriesImage _
          | Editor.SaveSeries _
          | Editor.ChangeSlug _
          | Editor.FetchPreview _
          | Editor.CheckPublish _
          | Editor.PublishPost _
          | Editor.ReadDeploy _
          | Editor.WaitToPollDeploy _
          | Editor.RetryDeploy _
          | Editor.DiscardPending _
          | Editor.UnpublishPost _
          | Editor.ChangePostDate _
          | Editor.DeletePost _
          | Editor.LoadRevisions _
          | Editor.RestoreRevision _
          | Editor.StartDelivery _
          | Editor.Reload _
          | Editor.Focus _
          | Editor.MoveToBodyLine _ -> [])

      match routerMsg, calls with
      | Router.UrlChanged _, calls -> next, Some(Navigate(Router.PushUrl(Route.path model.Router.Route), calls))
      | Router.LinkFollowed _, [] -> next, None
      | Router.LinkFollowed _, calls -> next, Some(Send calls)
    | None -> model, None
  | Router msg ->
    let router, effect = Router.update msg model.Router

    let page, pageCalls =
      if not (Route.isSamePage router.Route model.Router.Route) then
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
      let sync = if Notifications.canSync model.Notifications.Environment then [ SyncDevice ] else []
      next, Some(Send([ LoadQueue ] @ snd (enter model.Router.Route model) @ sync))
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
    | Rebuild.Answered(Ok _) ->
      let siteDeploy = SiteDeploy.restart model.SiteDeploy
      { next with SiteDeploy = siteDeploy }, Some(Send [ PollSiteDeploy siteDeploy.Generation ])
    | _ -> next, effect |> Option.map (fun Rebuild.Dispatch -> Send [ RebuildSite ])
  | SiteDeploy(SiteDeploy.PollElapsed _) when model.Router.Route <> Route.Dashboard && model.Router.Route <> Route.Series ->
    model, None
  | SiteDeploy msg ->
    let siteDeploy, effect = SiteDeploy.update msg model.SiteDeploy
    let next = { model with SiteDeploy = siteDeploy }

    match msg with
    | SiteDeploy.Answered(Error Api.Refused)
    | SiteDeploy.Polled(_, Error Api.Refused) -> update (Session Session.AccountRefused) next
    | SiteDeploy.Answered _
    | SiteDeploy.Polled _
    | SiteDeploy.PollElapsed _ ->
      next,
      effect
      |> Option.map (fun effect ->
        match effect with
        | SiteDeploy.Poll generation -> Send [ PollSiteDeploy generation ]
        | SiteDeploy.WaitToPoll generation -> WaitToPollSiteDeploy generation)
  | Posts(Posts.PollElapsed _) when not (Route.isPosts model.Router.Route) -> model, None
  | Posts msg ->
    let posts, effect = Posts.update msg model.Posts
    let next = { model with Posts = posts }

    match msg with
    | Posts.Answered(Error Api.Refused)
    | Posts.DraftCreated(Error Api.Refused) -> update (Session Session.AccountRefused) next
    | Posts.DraftCreated(Ok draft) -> update (Router(Router.LinkFollowed(Route.path (Route.Post draft.Slug)))) next
    | Posts.Polled(_, Error Api.Refused) -> update (Session Session.AccountRefused) next
    | Posts.Answered _
    | Posts.Polled _
    | Posts.PollElapsed _
    | Posts.DraftCreated _
    | Posts.NoticeDismissed
    | Posts.NewDraftClicked ->
      next,
      effect
      |> Option.map (fun effect ->
        match effect with
        | Posts.CreateDraft -> Send [ CreateDraft ]
        | Posts.Poll generation -> Send [ PollPosts generation ]
        | Posts.WaitToPoll generation -> WaitToPollPosts generation)
  | Editor Editor.PreviewClosed when layerOpenOverEditor model -> model, None
  | Editor msg ->
    match model.Editor with
    | None -> model, None
    | Some editor ->
      let editor, effect = Editor.update msg editor
      let next = { model with Editor = Some editor }

      match msg, editor with
      | Editor.Answered(_, Error Api.Refused), _
      | Editor.SaveAnswered(_, _, Error Api.Refused), _
      | Editor.SlugChangeAnswered(_, Error Api.Refused), _
      | Editor.DetailsMsg(Details.OptionsAnswered(Error Api.Refused)), _
      | Editor.DetailsMsg(Details.NewSeriesMsg(SeriesForm.ImageUploaded(Error Api.Refused))), _
      | Editor.DetailsMsg(Details.NewSeriesAnswered(Error Api.Refused)), _
      | Editor.PreviewAnswered(_, Error Api.Refused), _
      | Editor.PublishCheckAnswered(_, _, Error Api.Refused), _
      | Editor.PublishAnswered(_, Error Api.Refused), _
      | Editor.DeployMsg(_, Deploy.StatusRead(Error Api.Refused)), _
      | Editor.DeployMsg(_, Deploy.Retried(Error Api.Refused)), _
      | Editor.UnpublishAnswered(_, Error Api.Refused), _
      | Editor.ChangeDateAnswered(_, Error Api.Refused), _
      | Editor.DeleteAnswered(_, Error Api.Refused), _
      | Editor.RevisionsAnswered(_, Error Api.Refused), _
      | Editor.RestoreAnswered(_, Error Api.Refused), _
      | Editor.DiscardAnswered(_, Error Api.Refused), _ -> update (Session Session.AccountRefused) next
      | Editor.DeleteAnswered(_, Ok deleted), _ ->
        let notice = { next with Posts = { next.Posts with Notice = deleted.Warnings } }
        update (Router(Router.LinkFollowed(Route.path (Route.Posts PostFilter.All)))) notice
      | _, Editor.Loaded({ LeaveTo = Some path } as document) when not (Editor.needsSave document) ->
        update (Router(Router.LinkFollowed path)) next
      | _ ->
        next,
        effect
        |> Option.map (fun effect ->
          match effect with
          | Editor.Autosave version -> Autosave version
          | Editor.Save request -> Send [ SavePost request ]
          | Editor.LoadDetailsOptions -> Send [ LoadDetailsOptions ]
          | Editor.UploadSeriesImage file -> Send [ UploadNewSeriesImage file ]
          | Editor.SaveSeries request -> Send [ SaveNewSeries request ]
          | Editor.ChangeSlug request -> Send [ ChangeSlug request ]
          | Editor.FetchPreview request -> Send [ PreviewPost request ]
          | Editor.CheckPublish(request, choice) -> Send [ CheckPublish(request, choice) ]
          | Editor.PublishPost request -> Send [ PublishPost request ]
          | Editor.ReadDeploy slug -> Send [ LoadPostDeploy slug ]
          | Editor.WaitToPollDeploy(slug, chain) -> WaitToPollDeploy(slug, chain)
          | Editor.RetryDeploy slug -> Send [ RetryPostDeploy slug ]
          | Editor.DiscardPending request -> Send [ DiscardPending request ]
          | Editor.UnpublishPost request -> Send [ UnpublishPost request ]
          | Editor.ChangePostDate request -> Send [ ChangePostDate request ]
          | Editor.DeletePost request -> Send [ DeletePost request ]
          | Editor.LoadRevisions slug -> Send [ LoadRevisions slug ]
          | Editor.RestoreRevision request -> Send [ RestoreRevision request ]
          | Editor.StartDelivery request -> Send [ StartDelivery request ]
          | Editor.Reload slug -> Send [ LoadPost slug ]
          | Editor.Focus elementId -> Focus elementId
          | Editor.MoveToBodyLine line -> MoveToBodyLine line)
  | Upload msg ->
    let uploads, effect = Upload.update msg model.Uploads
    let next = { model with Uploads = uploads }

    match msg with
    | Upload.Answered(_, Error Api.Refused) -> update (Session Session.AccountRefused) next
    | _ -> next, effect |> Option.map (fun (Upload.Send(id, file)) -> Send [ UploadAsset(id, file) ])
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
      let effect =
        effect
        |> Option.map (fun effect ->
          match effect with
          | Queue.Trust comment -> Send [ TrustCommenter comment ]
          | Queue.Moderate(comment, action) -> Send [ Moderate(comment, action) ])

      withBadge model.Queue queue (next, effect)
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
      let effect =
        effect
        |> Option.map (fun (PostComments.Moderate(slug, comment, action)) -> Send [ ModeratePostComment(slug, comment, action) ])

      withBadge model.Queue next.Queue (next, effect)
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
  | Series(Series.DeleteChosen) when model.Overlay <> Some DeleteSeriesDialog -> model, None
  | Series msg ->
    let series, effect = Series.update msg model.Series

    let next =
      { model with
          Series = series
          Overlay =
            match msg with
            | Series.DeleteChosen -> None
            | _ -> model.Overlay }

    match msg with
    | Series.Answered(Error Api.Refused)
    | Series.FormMsg(SeriesForm.ImageUploaded(Error Api.Refused))
    | Series.SaveAnswered(Error Api.Refused)
    | Series.DeleteAnswered(_, Error Api.Refused) -> update (Session Session.AccountRefused) next
    | Series.SaveAnswered(Ok _) ->
      let siteDeploy = SiteDeploy.restart model.SiteDeploy
      { next with SiteDeploy = siteDeploy }, Some(Send [ LoadSeries; PollSiteDeploy siteDeploy.Generation ])
    | _ ->
      next,
      effect
      |> Option.map (fun effect ->
        match effect with
        | Series.Load -> Send [ LoadSeries ]
        | Series.UploadImage file -> Send [ UploadSeriesImage file ]
        | Series.SaveSeries request -> Send [ SaveSeries request ]
        | Series.DeleteSeries(slug, eTag) -> Send [ DeleteSeries(slug, eTag) ])
  | Notifications msg ->
    let notifications, effect = Notifications.update msg model.Notifications
    let next = { model with Notifications = notifications }

    match msg with
    | Notifications.DevicesAnswered(Error Api.Refused)
    | Notifications.RegisterAnswered(Error Api.Refused)
    | Notifications.Removed(_, Error Api.Refused) -> update (Session Session.AccountRefused) next
    | Notifications.RegisterAnswered(Ok(Some _)) when model.Router.Route = Route.Settings -> next, Some(Send [ LoadDevices ])
    | _ ->
      // Before the session is active, the start sends the sync.
      let sync call =
        if Session.isActive model.Session then Some(Send [ call ]) else None

      next,
      effect
      |> Option.bind (fun effect ->
        match effect with
        | Notifications.Subscribe -> Some Subscribe
        | Notifications.Unsubscribe -> Some Unsubscribe
        | Notifications.Register keys -> Some(Send [ RegisterDevice keys ])
        | Notifications.Sync -> sync SyncDevice
        | Notifications.Resync -> sync ResyncDevice
        | Notifications.Remove deviceId -> Some(Send [ RemoveDevice deviceId ]))
  | NewCommentAnnounced -> model, (if Session.isActive model.Session then Some(Send [ LoadQueue ]) else None)
  | OpenRequested target -> update (Router(Router.LinkFollowed target)) model
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
  | Some DeleteSeriesDialog, _
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
  | Some DeleteSeriesDialog, _
  | Some MoreSheet, _
  | Some SignOutDialog, _
  | None, _ -> None

let deleteSubscriptionChosen (model: Model) =
  match model.Overlay, model.Router.Route, model.Subscriptions.Sheet with
  | Some DeleteSubscriptionDialog, Route.Subscriptions _, Some sheet ->
    Subscriptions.tryOpened model.Subscriptions
    |> Option.map (fun _ -> Subscriptions(Subscriptions.Chosen(sheet.Id, Subscriptions.Delete)))
  | Some DeleteSubscriptionDialog, _, _
  | Some DeleteSeriesDialog, _, _
  | Some DeleteCommenterDialog, _, _
  | Some(DeleteDialog _), _, _
  | Some MoreSheet, _, _
  | Some SignOutDialog, _, _
  | None, _, _ -> None

let deleteSeriesChosen (model: Model) =
  match model.Overlay, model.Router.Route, model.Series.Form with
  | Some DeleteSeriesDialog, Route.Series, Some form when Series.canDelete form -> Some(Series Series.DeleteChosen)
  | Some DeleteSeriesDialog, _, _
  | Some DeleteSubscriptionDialog, _, _
  | Some DeleteCommenterDialog, _, _
  | Some(DeleteDialog _), _, _
  | Some MoreSheet, _, _
  | Some SignOutDialog, _, _
  | None, _, _ -> None
