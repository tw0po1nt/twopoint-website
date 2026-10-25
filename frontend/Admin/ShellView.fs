[<RequireQualifiedAccess>]
module TwoPoint.Admin.ShellView

open TwoPoint.Site.Widgets

open Browser
open Browser.Types
open Fable.Core
open Oxpecker.Solid
open Oxpecker.Solid.Aria

let private fail (call: Shell.Call) =
  match call with
  | Shell.LoadDashboard -> Shell.Dashboard(Dashboard.Answered(Error Api.Unanswered))
  | Shell.LoadPosts -> Shell.Posts(Posts.Answered(Error Api.Unanswered))
  | Shell.PollPosts -> Shell.Posts(Posts.Polled(Error Api.Unanswered))
  | Shell.LoadQueue -> Shell.Queue(Queue.Answered(Error Api.Unanswered))
  | Shell.TrustCommenter comment -> Shell.Queue(Queue.Trusted(comment.Comment.Id, Error Api.Unanswered))
  | Shell.Moderate(comment, action) -> Shell.Queue(Queue.Done(comment.Comment.Id, action, Error Api.Unanswered))
  | Shell.LoadPostComments slug -> Shell.PostComments(PostComments.Answered(slug, Error Api.Unanswered))
  | Shell.ModeratePostComment(_, comment, action) ->
    Shell.PostComments(PostComments.Done(comment.Id, action, Error Api.Unanswered))
  | Shell.LoadCommenters -> Shell.Commenters(Commenters.Answered(Error Api.Unanswered))
  | Shell.LoadCommenter commenterId -> Shell.Commenter(Commenter.Answered(commenterId, Error Api.Unanswered))
  | Shell.ChangeCommenter(commenterId, action) ->
    Shell.Commenter(Commenter.Done(commenterId, action, Error Api.Unanswered))
  | Shell.LoadSubscriptions -> Shell.Subscriptions(Subscriptions.Answered(Error Api.Unanswered))
  | Shell.LoadSubscriptionDeliveries subscriptionId ->
    Shell.Subscriptions(Subscriptions.DeliveriesAnswered(subscriptionId, Error Api.Unanswered))
  | Shell.CancelSubscription subscriptionId -> Shell.Subscriptions(Subscriptions.Canceled(subscriptionId, Error Api.Unanswered))
  | Shell.DeleteSubscription subscriptionId -> Shell.Subscriptions(Subscriptions.Deleted(subscriptionId, Error Api.Unanswered))
  | Shell.LoadDevices -> Shell.Notifications(Notifications.DevicesAnswered(Error Api.Unanswered))
  | Shell.SyncDevice
  | Shell.ResyncDevice
  | Shell.RegisterDevice _ -> Shell.Notifications(Notifications.RegisterAnswered(Error Api.Unanswered))
  | Shell.RemoveDevice deviceId -> Shell.Notifications(Notifications.Removed(deviceId, Error Api.Unanswered))
  | Shell.RebuildSite -> Shell.Rebuild(Rebuild.Answered(Error Api.Unanswered))
  | Shell.RetryPostsDeploy -> Shell.Posts(Posts.Retried(Error Api.Unanswered))
  | Shell.CreateDraft -> Shell.Posts(Posts.DraftCreated(Error Api.Unanswered))
  | Shell.LoadPost slug -> Shell.Editor(Editor.Answered(slug, Error Api.Unanswered))
  | Shell.SavePost request -> Shell.Editor(Editor.SaveAnswered(request.Slug, request.Version, Error Api.Unanswered))
  | Shell.LoadDetailsOptions -> Shell.Editor(Editor.DetailsMsg(Details.OptionsAnswered(Error Api.Unanswered)))
  | Shell.ChangeSlug request -> Shell.Editor(Editor.SlugChangeAnswered(request.Slug, Error Api.Unanswered))
  | Shell.PreviewPost request -> Shell.Editor(Editor.PreviewAnswered(request.Slug, Error Api.Unanswered))
  | Shell.CheckPublish(request, choice) ->
    Shell.Editor(Editor.PublishCheckAnswered(request.Slug, choice, Error Api.Unanswered))
  | Shell.PublishPost request -> Shell.Editor(Editor.PublishAnswered(request.Slug, Error Api.Unanswered))
  | Shell.LoadPostDeploy slug -> Shell.Editor(Editor.DeployMsg(slug, Deploy.StatusRead(Error Api.Unanswered)))
  | Shell.RetryPostDeploy slug -> Shell.Editor(Editor.DeployMsg(slug, Deploy.Retried(Error Api.Unanswered)))
  | Shell.UploadAsset(id, _) -> Shell.Upload(Upload.Answered(id, Error Api.Unanswered))

let private moderate (token: string) (slug: string) (comment: Api.Comment) (action: Queue.Action) =
  match action with
  // The trust of Approve and trust is a separate call.
  | Queue.Approve
  | Queue.ApproveAndTrust -> Api.changeApproval (Config.api ()) token slug comment Api.Approval.Approved
  | Queue.Reject -> Api.changeApproval (Config.api ()) token slug comment Api.Approval.Rejected
  | Queue.Delete -> Api.deleteComment (Config.api ()) token slug comment

let private changeCommenter (token: string) (commenterId: string) (action: Commenter.Action) =
  match Commenter.statusAfter action with
  | Some status -> Api.changeCommenterStatus (Config.api ()) token commenterId status
  | None -> Api.deleteCommenter (Config.api ()) token commenterId

let private register (token: string) (keys: PushKeys) =
  Api.registerDevice (Config.api ()) token keys
  |> Promise.map (Result.map Some >> Notifications.RegisterAnswered >> Shell.Notifications)

let private answer (push: Push) (call: Shell.Call) (token: string) : JS.Promise<Shell.Msg> =
  match call with
  | Shell.LoadDashboard -> Api.getPosts (Config.api ()) token |> Promise.map (Dashboard.Answered >> Shell.Dashboard)
  | Shell.LoadPosts -> Api.getPosts (Config.api ()) token |> Promise.map (Posts.Answered >> Shell.Posts)
  | Shell.PollPosts -> Api.getPosts (Config.api ()) token |> Promise.map (Posts.Polled >> Shell.Posts)
  | Shell.LoadQueue ->
    Api.getModerationQueue (Config.api ()) token
    |> Promise.map (Queue.Answered >> Shell.Queue)
  | Shell.TrustCommenter comment ->
    Api.changeCommenterStatus (Config.api ()) token comment.Comment.Commenter.Id Api.Trusted
    |> Promise.map (fun result -> Shell.Queue(Queue.Trusted(comment.Comment.Id, result)))
  | Shell.Moderate(comment, action) ->
    moderate token comment.PostSlug comment.Comment action
    |> Promise.map (fun result -> Shell.Queue(Queue.Done(comment.Comment.Id, action, result)))
  | Shell.LoadPostComments slug ->
    Api.getPostComments (Config.api ()) token slug
    |> Promise.map (fun result -> Shell.PostComments(PostComments.Answered(slug, result)))
  | Shell.ModeratePostComment(slug, comment, action) ->
    moderate token slug comment action
    |> Promise.map (fun result -> Shell.PostComments(PostComments.Done(comment.Id, action, result)))
  | Shell.LoadCommenters ->
    Api.getCommenters (Config.api ()) token
    |> Promise.map (Commenters.Answered >> Shell.Commenters)
  | Shell.LoadCommenter commenterId ->
    Api.getCommenter (Config.api ()) token commenterId
    |> Promise.map (fun result -> Shell.Commenter(Commenter.Answered(commenterId, result)))
  | Shell.ChangeCommenter(commenterId, action) ->
    changeCommenter token commenterId action
    |> Promise.map (fun result -> Shell.Commenter(Commenter.Done(commenterId, action, result)))
  | Shell.LoadSubscriptions ->
    Api.getSubscriptions (Config.api ()) token
    |> Promise.map (Subscriptions.Answered >> Shell.Subscriptions)
  | Shell.LoadSubscriptionDeliveries subscriptionId ->
    Api.getSubscriptionDeliveries (Config.api ()) token subscriptionId
    |> Promise.map (fun result -> Shell.Subscriptions(Subscriptions.DeliveriesAnswered(subscriptionId, result)))
  | Shell.CancelSubscription subscriptionId ->
    Api.cancelSubscription (Config.api ()) token subscriptionId
    |> Promise.map (fun result -> Shell.Subscriptions(Subscriptions.Canceled(subscriptionId, result)))
  | Shell.DeleteSubscription subscriptionId ->
    Api.deleteSubscription (Config.api ()) token subscriptionId
    |> Promise.map (fun result -> Shell.Subscriptions(Subscriptions.Deleted(subscriptionId, result)))
  | Shell.LoadDevices ->
    Api.getDevices (Config.api ()) token
    |> Promise.map (Notifications.DevicesAnswered >> Shell.Notifications)
  | Shell.SyncDevice ->
    push.subscription ()
    |> Promise.bind (fun keys ->
      match keys with
      | Some keys -> register token keys
      | None -> Promise.lift (Shell.Notifications(Notifications.RegisterAnswered(Ok None))))
  | Shell.ResyncDevice ->
    push.subscription ()
    |> Promise.bind (fun keys ->
      match keys with
      | Some _ -> Promise.lift keys
      | None -> push.subscribe ())
    |> Promise.bind (fun keys ->
      match keys with
      | Some keys -> register token keys
      | None -> Promise.lift (Shell.Notifications(Notifications.RegisterAnswered(Ok None))))
  | Shell.RegisterDevice keys -> register token keys
  | Shell.RemoveDevice deviceId ->
    Api.deleteDevice (Config.api ()) token deviceId
    |> Promise.map (fun result -> Shell.Notifications(Notifications.Removed(deviceId, result)))
  | Shell.RebuildSite -> Api.rebuildSite (Config.api ()) token |> Promise.map (Rebuild.Answered >> Shell.Rebuild)
  | Shell.RetryPostsDeploy -> Api.rebuildSite (Config.api ()) token |> Promise.map (Posts.Retried >> Shell.Posts)
  | Shell.CreateDraft -> Api.createDraft (Config.api ()) token |> Promise.map (Posts.DraftCreated >> Shell.Posts)
  | Shell.LoadPost slug ->
    Api.getPost (Config.api ()) token slug
    |> Promise.map (fun result -> Shell.Editor(Editor.Answered(slug, result)))
  | Shell.SavePost request ->
    Api.savePost
      (Config.api ())
      token
      request.Slug
      request.ETag
      request.Fields.Title
      request.Fields.Excerpt
      request.Fields.Body
      request.Details
    |> Promise.map (fun result -> Shell.Editor(Editor.SaveAnswered(request.Slug, request.Version, result)))
  | Shell.LoadDetailsOptions ->
    Api.getTerms (Config.api ()) token
    |> Promise.bind (fun terms ->
      match terms with
      | Ok terms ->
        Api.getSeries (Config.api ()) token
        |> Promise.map (Result.map (fun series -> { Details.Terms = terms; Details.Series = series }))
      | Error failure -> Promise.lift (Error failure))
    |> Promise.map (Details.OptionsAnswered >> Editor.DetailsMsg >> Shell.Editor)
  | Shell.ChangeSlug request ->
    Api.changeSlug (Config.api ()) token request.Slug request.ETag request.NewSlug
    |> Promise.map (fun result -> Shell.Editor(Editor.SlugChangeAnswered(request.Slug, result)))
  | Shell.PreviewPost request ->
    Api.previewPost
      (Config.api ())
      token
      request.Slug
      request.Fields.Title
      request.Fields.Excerpt
      request.Fields.Body
      request.Details
    |> Promise.map (fun result -> Shell.Editor(Editor.PreviewAnswered(request.Slug, result)))
  | Shell.CheckPublish(request, choice) ->
    Api.checkPublish (Config.api ()) token request.Slug request.ETag request.Target
    |> Promise.map (fun result -> Shell.Editor(Editor.PublishCheckAnswered(request.Slug, choice, result)))
  | Shell.PublishPost request ->
    Api.publishPost (Config.api ()) token request.Slug request.ETag request.Target
    |> Promise.map (fun result -> Shell.Editor(Editor.PublishAnswered(request.Slug, result)))
  | Shell.LoadPostDeploy slug ->
    Api.getPostDeploy (Config.api ()) token slug
    |> Promise.map (fun result -> Shell.Editor(Editor.DeployMsg(slug, Deploy.StatusRead result)))
  | Shell.RetryPostDeploy slug ->
    Api.rebuildSite (Config.api ()) token
    |> Promise.map (fun result -> Shell.Editor(Editor.DeployMsg(slug, Deploy.Retried result)))
  | Shell.UploadAsset(id, file) ->
    Api.uploadAsset (Config.api ()) token file
    |> Promise.map (fun result -> Shell.Upload(Upload.Answered(id, result)))

let private send (signIn: SignIn) (push: Push) (dispatch: Shell.Msg -> unit) (call: Shell.Call) =
  signIn.token ()
  |> Promise.bind (fun token ->
    match token with
    | Some token -> answer push call token
    | None -> Promise.lift (Shell.Session(Session.InteractionRequired call)))
  |> Promise.catch (fun error ->
    console.error ("admin: no token for the API call.", error)
    fail call)
  |> Promise.map dispatch
  |> Promise.start

let private signInWith (signIn: SignIn) (push: Push) (effect: Session.Effect<Shell.Call>) (dispatch: Shell.Msg -> unit) =
  let session = Shell.Session >> dispatch

  let report (what: string) (msg: Session.Msg<Shell.Call>) (error: exn) =
    console.error ($"admin: {what}", error)
    session msg

  match effect with
  | Session.Start ->
    signIn.start ()
    |> Promise.map (Session.Started >> session)
    |> Promise.catch (report "the sign-in did not start." Session.SignInFailed)
    |> Promise.start
  | Session.SignInByRedirect ->
    signIn.signIn ()
    |> Promise.catch (report "the Entra sign-in did not open." Session.SignInFailed)
    |> Promise.start
  | Session.SignInByPopup ->
    signIn.signInByPopup ()
    |> Promise.map (fun () -> session Session.Renewed)
    |> Promise.catch (report "the sign-in popup did not complete." Session.RenewFailed)
    |> Promise.start
  | Session.Retry calls -> calls |> List.iter (send signIn push dispatch)
  | Session.SignOut ->
    signIn.signOut ()
    |> Promise.catch (report "the Entra sign-out did not open." Session.SignOutFailed)
    |> Promise.start

let private subscribe (push: Push) (dispatch: Shell.Msg -> unit) =
  let answered permission result =
    dispatch (Shell.Notifications(Notifications.Subscribed(permission, result)))

  let permission () = Notifications.permissionOf (push.permission ())

  push.subscribe ()
  |> Promise.map (fun keys -> answered (permission ()) (Ok keys))
  |> Promise.catch (fun error ->
    console.error ("admin: the push subscription failed.", error)
    answered (permission ()) (Error "The browser did not subscribe this device to notifications."))
  |> Promise.start

let private perform (signIn: SignIn) (push: Push) (effect: Shell.Effect) (dispatch: Shell.Msg -> unit) =
  match effect with
  | Shell.Navigate(Router.PushUrl path, calls) ->
    history.pushState (null, "", path)
    calls |> List.iter (send signIn push dispatch)
  | Shell.SignIn effect -> signInWith signIn push effect dispatch
  | Shell.Send calls -> calls |> List.iter (send signIn push dispatch)
  | Shell.Subscribe -> subscribe push dispatch
  | Shell.SetBadge count -> push.setBadge count
  | Shell.Unsubscribe ->
    push.unsubscribe ()
    |> Promise.catch (fun error -> console.error ("admin: the push unsubscribe failed.", error))
    |> Promise.start
  | Shell.Focus elementId -> EditorView.focus elementId
  | Shell.MoveToBodyLine line -> EditorView.moveToBodyLine line
  | Shell.Autosave version ->
    window.setTimeout ((fun () -> dispatch (Shell.Editor(Editor.AutosaveElapsed version))), Editor.autosaveDelay)
    |> ignore
  | Shell.WaitToPollDeploy slug ->
    window.setTimeout (
      (fun () -> dispatch (Shell.Editor(Editor.DeployMsg(slug, Deploy.PollElapsed)))),
      Deploy.pollDelay
    )
    |> ignore
  | Shell.WaitToPollPosts ->
    window.setTimeout ((fun () -> dispatch (Shell.Posts Posts.PollElapsed)), Deploy.pollDelay) |> ignore

let private site = "https://twopoint.dev"

let private follow (dispatch: Router.Msg -> unit) (route: Route) (event: MouseEvent) =
  if event.button = 0. && not (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) then
    event.preventDefault ()
    dispatch (Router.LinkFollowed(Route.path route))

let private currentMark (isCurrent: bool) = if isCurrent then "page" else null

[<SolidComponent>]
let private routeLink (dispatch: Router.Msg -> unit) (route: Route) (text: string) =
  a (href = Route.path route, onClick = follow dispatch route) { text }

[<SolidComponent>]
let private brand (dispatch: Router.Msg -> unit) =
  a (class' = "brand", href = Route.path Dashboard, ariaLabel = "twopoint admin", onClick = follow dispatch Dashboard) {
    span (class' = "mark") { "2·" }

    span (class' = "brand-text") {
      "twopoint "
      span (class' = "muted") { "admin" }
    }
  }

[<SolidComponent>]
let private queueCount (item: Route) (count: unit -> int option) =
  Fragment() {
    Show(when' = (item = ModerationQueue && (count () |> Option.exists (fun value -> value > 0)))) {
      Parts.count (count () |> Option.defaultValue 0)
    }
  }

[<SolidComponent>]
let private sideLink (dispatch: Router.Msg -> unit) (current: unit -> Route) (count: unit -> int option) (item: Route) =
  a (
    class' = "side-link",
    href = Route.path item,
    title = Router.title item,
    ariaCurrent = currentMark (Router.isCurrent item (current ())),
    onClick = follow dispatch item
  ) {
    Icon.view (Icon.ofRoute item)
    span (class' = "nav-label") { Router.title item }
    queueCount item count
  }

[<SolidComponent>]
let private tabLink
  (dispatch: Router.Msg -> unit)
  (current: unit -> Route)
  (count: unit -> int option)
  (openMore: unit -> unit)
  (tab: Router.Tab)
  =
  match tab with
  | Router.Page item ->
    Fragment() {
      a (
        href = Route.path item,
        ariaCurrent = currentMark (Router.isCurrentTab tab (current ())),
        onClick = follow dispatch item
      ) {
        span (class' = "tab-icon") {
          Icon.view (Icon.ofRoute item)
          queueCount item count
        }

        span (class' = "nav-label") { Router.tabTitle tab }
      }
    }
  | Router.More ->
    Fragment() {
      button (
        type' = "button",
        ariaCurrent = currentMark (Router.isCurrentTab tab (current ())),
        ariaHasPopup = "dialog",
        onClick = fun _ -> openMore ()
      ) {
        span (class' = "tab-icon") { Icon.view Icon.Menu }
        span (class' = "nav-label") { Router.tabTitle tab }
      }
    }

[<SolidComponent>]
let private accountMenu (id: string) (placement: Menu.Placement) (name: unit -> string) (signOut: unit -> unit) =
  Parts.menu
    id
    placement
    (Fragment() {
      Parts.menuLabel (name ())

      Parts.menuLink
        site
        (Fragment() {
          Icon.view Icon.External
          "Open twopoint.dev"
        })

      Parts.menuSeparator ()

      Parts.menuItem
        id
        Parts.Plain
        signOut
        (Fragment() {
          Icon.view Icon.SignOut
          "Sign out"
        })
    })

[<SolidComponent>]
let private dashboard (model: Dashboard.Model) =
  match model with
  | Dashboard.Waiting ->
    Fragment() {
      div (class' = "stat-grid", ariaBusy = true) {
        div (class' = "card card-body") {
          Parts.skeleton "40%"
          Parts.skeleton "24px"
        }

        div (class' = "card card-body") {
          Parts.skeleton "60%"
          Parts.skeleton "24px"
        }
      }
    }
  | Dashboard.Loaded summary ->
    Fragment() {
      div (class' = "stat-grid") {
        Parts.stat "Posts" (string summary.Posts)
        Parts.stat "New comments" (string summary.NewComments)
      }
    }
  | Dashboard.NotLoaded reason ->
    Fragment() { Parts.Alert(tone = Parts.Bad, title = "The dashboard did not load.").Has(p () { reason }) }

[<SolidComponent>]
let private rebuild (dispatch: Shell.Msg -> unit) (model: Rebuild.Model) =
  div (class' = "card card-body rebuild") {
    div (class' = "rebuild-row") {
      div (class' = "grow") {
        h3 () { "Site" }
        p (class' = "muted small rebuild-text", ariaLive = "polite") { Rebuild.text model }
      }

      Parts
        .Button(disabled = Rebuild.isSending model, onClick = fun _ -> dispatch (Shell.Rebuild Rebuild.Clicked))
        .Has(Fragment() { "Rebuild site" })
    }

    Show(when' = Rebuild.failed model) { Parts.Alert(tone = Parts.Bad).Has(p () { Rebuild.reason model }) }
  }

let private statusTone (status: Api.CommenterStatus) =
  match status with
  | Api.New -> Parts.Plain
  | Api.Trusted -> Parts.Good
  | Api.Banned -> Parts.Bad

[<SolidComponent>]
let private commenterHead (dispatch: Shell.Msg -> unit) (commenter: Api.Commenter) =
  let route = Shell.Router >> dispatch

  div (class' = "queue-head") {
    Parts.avatar commenter.Name

    div (class' = "grow") {
      p (class' = "strong") {
        routeLink route (Commenter commenter.Id) commenter.Name
        " "
        Parts.badge (statusTone commenter.Status) (Queue.statusText commenter.Status)
      }

      p (class' = "muted small ellipsis") { commenter.Email }
    }
  }

[<SolidComponent>]
let private actionButton
  (dispatch: Shell.Msg -> unit)
  (commentId: string)
  (choose: Queue.Action -> unit)
  (sending: bool)
  (action: Queue.Action)
  =
  match action with
  | Queue.Approve ->
    Fragment() {
      Parts
        .Button(kind = Parts.Accent, size = Parts.Small, disabled = sending, onClick = fun _ -> choose Queue.Approve)
        .Has(Fragment() { "Approve" })
    }
  | Queue.ApproveAndTrust ->
    Fragment() {
      Parts
        .Button(size = Parts.Small, disabled = sending, onClick = fun _ -> choose Queue.ApproveAndTrust)
        .Has(Fragment() { "Approve and trust" })
    }
  | Queue.Reject ->
    Fragment() {
      Parts.Button(size = Parts.Small, disabled = sending, onClick = fun _ -> choose Queue.Reject).Has(Fragment() { "Reject" })
    }
  | Queue.Delete ->
    Fragment() {
      Parts
        .Button(
          kind = Parts.Ghost,
          size = Parts.Small,
          disabled = sending,
          onClick = fun _ -> dispatch (Shell.Opened(Shell.DeleteDialog commentId))
        )
        .Has(Fragment() { "Delete" })
    }

[<SolidComponent>]
let private rowEnd
  (dispatch: Shell.Msg -> unit)
  (commentId: string)
  (choose: Queue.Action -> unit)
  (state: Queue.RowState)
  (actions: Queue.Action list)
  =
  Fragment() {
    Show(when' = (Queue.failure state).IsSome) {
      Parts.Alert(tone = Parts.Bad).Has(p () { Queue.failure state |> Option.defaultValue "" })
    }

    div (class' = "actions") {
      For(each = Array.ofList actions) {
        yield fun action _ -> actionButton dispatch commentId choose (Progress.isSending state) action
      }
    }
  }

[<SolidComponent>]
let private queueRow (dispatch: Shell.Msg -> unit) (row: Queue.Row) =
  let comment = row.Comment.Comment
  let choose action = dispatch (Shell.Queue(Queue.Chosen(comment.Id, action)))

  article (id = comment.Id, class' = "card card-body queue-row") {
    commenterHead dispatch comment.Commenter
    p (class' = "muted small") { $"On {row.Comment.PostTitle}" }
    p (class' = "queue-text") { comment.Content }
    rowEnd dispatch comment.Id choose row.State (Queue.actions comment.Commenter.Status)
  }

[<SolidComponent>]
let private queue (dispatch: Shell.Msg -> unit) (model: Queue.Model) =
  match model with
  | Queue.Waiting ->
    Fragment() {
      div (class' = "card card-body", ariaBusy = true) {
        Parts.skeleton "40%"
        Parts.skeleton "80%"
      }
    }
  | Queue.Loaded [] ->
    Fragment() {
      Parts.emptyState Icon.Inbox "The moderation queue is empty." (p () { "Each new comment comes here." })
    }
  | Queue.Loaded rows ->
    Fragment() {
      div (class' = "queue") { For(each = Array.ofList rows) { yield fun row _ -> queueRow dispatch row } }
    }
  | Queue.NotLoaded reason ->
    Fragment() { Parts.Alert(tone = Parts.Bad, title = "The moderation queue did not load.").Has(p () { reason }) }

let private approvalTone (approval: Api.CommentApproval) =
  match approval with
  | Api.CommentApproval.New -> Parts.Warn
  | Api.CommentApproval.Approved -> Parts.Good
  | Api.CommentApproval.Rejected -> Parts.Bad

[<SolidComponent>]
let private postCommentRow (dispatch: Shell.Msg -> unit) (row: PostComments.Row) =
  let comment = row.Comment
  let choose action = dispatch (Shell.PostComments(PostComments.Chosen(comment.Id, action)))

  article (id = comment.Id, class' = "card card-body queue-row") {
    div (class' = "row-top") {
      commenterHead dispatch comment.Commenter
      Parts.badge (approvalTone comment.Status) (PostComments.approvalText comment.Status)
    }

    p (class' = "queue-text") { comment.Content }
    rowEnd dispatch comment.Id choose row.State (PostComments.actions comment.Status)
  }

[<SolidComponent>]
let private postCommentList (dispatch: Shell.Msg -> unit) (model: PostComments.Model) =
  match model.Comments, PostComments.shown model with
  | PostComments.Waiting, _ ->
    Fragment() {
      div (class' = "card card-body", ariaBusy = true) {
        Parts.skeleton "40%"
        Parts.skeleton "80%"
      }
    }
  | PostComments.Loaded _, [] ->
    Fragment() {
      Parts.emptyState Icon.Inbox (PostComments.emptyText model.Filter) (p () { "Each comment on the post comes here." })
    }
  | PostComments.Loaded _, rows ->
    Fragment() {
      div (class' = "queue") { For(each = Array.ofList rows) { yield fun row _ -> postCommentRow dispatch row } }
    }
  | PostComments.NotLoaded reason, _ ->
    Fragment() { Parts.Alert(tone = Parts.Bad, title = "The comments did not load.").Has(p () { reason }) }

[<SolidComponent>]
let private postComments (dispatch: Shell.Msg -> unit) (model: PostComments.Model) =
  let filter = PostComments.Filtered >> Shell.PostComments >> dispatch

  Fragment() {
    div (class' = "filter-bar") { Parts.tabs "Approval" PostComments.filters (fun () -> model.Filter) filter }
    postCommentList dispatch model
  }

[<SolidComponent>]
let private commenterRow (dispatch: Shell.Msg -> unit) (commenter: Api.Commenter) =
  article (id = commenter.Id, class' = "card card-body queue-row") {
    commenterHead dispatch commenter
    p (class' = "muted small") { $"First date: {Dates.dateText commenter.CreatedDate}" }
  }

[<SolidComponent>]
let private commenterList (dispatch: Shell.Msg -> unit) (model: Commenters.Model) =
  match model.Commenters, Commenters.shown model with
  | Commenters.Waiting, _ ->
    Fragment() {
      div (class' = "card card-body", ariaBusy = true) {
        Parts.skeleton "40%"
        Parts.skeleton "80%"
      }
    }
  | Commenters.Loaded _, [] ->
    Fragment() {
      Parts.emptyState
        Icon.Inbox
        (Commenters.emptyText model.Filter)
        (p () { "Each person who verifies an email address to comment comes here." })
    }
  | Commenters.Loaded _, commenters ->
    Fragment() {
      div (class' = "queue") {
        For(each = Array.ofList commenters) { yield fun commenter _ -> commenterRow dispatch commenter }
      }
    }
  | Commenters.NotLoaded reason, _ ->
    Fragment() { Parts.Alert(tone = Parts.Bad, title = "The commenters did not load.").Has(p () { reason }) }

[<SolidComponent>]
let private commenters (dispatch: Shell.Msg -> unit) (model: Commenters.Model) =
  let filter = Commenters.Filtered >> Shell.Commenters >> dispatch

  Fragment() {
    div (class' = "filter-bar") { Parts.tabs "Status" Commenters.filters (fun () -> model.Filter) filter }
    commenterList dispatch model
  }

[<SolidComponent>]
let private commenterAction (dispatch: Shell.Msg -> unit) (sending: bool) (action: Commenter.Action) =
  let choose () =
    match action with
    | Commenter.Delete -> dispatch (Shell.Opened Shell.DeleteCommenterDialog)
    | Commenter.Trust
    | Commenter.Untrust
    | Commenter.Ban
    | Commenter.Unban -> dispatch (Shell.Commenter(Commenter.Chosen action))

  let kind =
    match action with
    | Commenter.Trust -> Parts.Accent
    | Commenter.Delete -> Parts.Ghost
    | Commenter.Untrust
    | Commenter.Ban
    | Commenter.Unban -> Parts.Secondary

  Parts
    .Button(kind = kind, size = Parts.Small, disabled = sending, onClick = fun _ -> choose ())
    .Has(Fragment() { Commenter.label action })

[<SolidComponent>]
let private commenterComment (dispatch: Shell.Msg -> unit) (comment: Api.PostedComment) =
  let route = Shell.Router >> dispatch

  article (id = comment.Comment.Id, class' = "card card-body queue-row") {
    div (class' = "row-top") {
      p (class' = "small") {
        "On "
        routeLink route (PostComments comment.PostSlug) comment.PostTitle
        span (class' = "muted") { $" on {Dates.dateText comment.Comment.CreatedDate}" }
      }

      Parts.badge (approvalTone comment.Comment.Status) (PostComments.approvalText comment.Comment.Status)
    }

    p (class' = "queue-text") { comment.Comment.Content }
  }

[<SolidComponent>]
let private commenterPage (dispatch: Shell.Msg -> unit) (model: Commenter.Model) =
  match model.Page with
  | Commenter.Waiting ->
    Fragment() {
      div (class' = "card card-body", ariaBusy = true) {
        Parts.skeleton "40%"
        Parts.skeleton "80%"
      }
    }
  | Commenter.Loaded(page, state) ->
    let commenter = page.Commenter

    Fragment() {
      div (class' = "queue") {
        div (class' = "card card-body queue-row commenter") {
          div (class' = "queue-head") {
            Parts.avatar commenter.Name
            p (class' = "strong grow") { commenter.Name }
          }

          dl (class' = "fields") {
            dt () { "Email" }
            dd () { commenter.Email }
            dt () { "Status" }
            dd () { Parts.badge (statusTone commenter.Status) (Queue.statusText commenter.Status) }
            dt () { "First date" }
            dd () { Dates.dateText commenter.CreatedDate }
          }

          Show(when' = (Commenter.failure state).IsSome) {
            Parts.Alert(tone = Parts.Bad).Has(p () { Commenter.failure state |> Option.defaultValue "" })
          }

          div (class' = "actions") {
            For(each = Array.ofList (Commenter.actions commenter.Status)) {
              yield fun action _ -> commenterAction dispatch (Progress.isSending state) action
            }
          }
        }

        h2 () { "Comments" }

        Show(when' = List.isEmpty page.Comments) { p (class' = "muted") { "This commenter has no comments." } }
        For(each = Array.ofList page.Comments) { yield fun comment _ -> commenterComment dispatch comment }
      }
    }
  | Commenter.NotLoaded reason ->
    Fragment() { Parts.Alert(tone = Parts.Bad, title = "The commenter did not load.").Has(p () { reason }) }

let private stateTone (state: Api.PostState) =
  match state with
  | Api.Draft -> Parts.Plain
  | Api.Published -> Parts.Good
  | Api.Upcoming -> Parts.Info

let private deployTone (state: Api.DeployState) =
  match state with
  | Api.DeployState.Deploying -> Parts.Info
  | Api.DeployState.Deployed -> Parts.Good
  | Api.DeployState.DeployFailed -> Parts.Bad

[<SolidComponent>]
let private postRow (dispatch: Shell.Msg -> unit) (retrying: bool) (post: Api.Post) =
  let route = Shell.Router >> dispatch
  let stats = post.CommentStats
  let date = post.PublishDate |> Option.map Dates.dateText |> Option.defaultValue "No publish date"

  article (id = post.Slug, class' = "card card-body queue-row") {
    div (class' = "row-top") {
      p (class' = "strong") { post.Title }

      div () {
        Parts.badge (stateTone post.State) (Posts.stateText post.State)

        Show(when' = post.PendingEdit) { Parts.badge Parts.Warn "Pending edit" }

        Show(when' = Posts.showsDeploy post) { Parts.badge (deployTone post.Deploy) (Deploy.badgeText post.Deploy) }
      }
    }

    Show(when' = Posts.canRetry post) {
      p (class' = "small") {
        button (type' = "button", class' = "text", disabled = retrying, onClick = fun _ -> dispatch (Shell.Posts Posts.RetryClicked)) {
          "Retry"
        }
      }
    }

    p (class' = "muted small") { $"{post.Slug} · {date}" }

    p (class' = "muted small") {
      $"{stats.New} new, {stats.Approved} approved, {stats.Rejected} rejected comments · "
      routeLink route (PostComments post.Slug) "Comments"
    }
  }

[<SolidComponent>]
let private postList (dispatch: Shell.Msg -> unit) (model: Posts.Model) =
  match model.Posts, Posts.shown model with
  | Posts.Waiting, _ ->
    Fragment() {
      div (class' = "card card-body", ariaBusy = true) {
        Parts.skeleton "40%"
        Parts.skeleton "80%"
      }
    }
  | Posts.Loaded _, [] ->
    Fragment() {
      Parts.emptyState Icon.Inbox (Posts.emptyText model.Filter) (p () { "Each post of the post store comes here." })
    }
  | Posts.Loaded _, posts ->
    Fragment() {
      div (class' = "queue") { For(each = Array.ofList posts) { yield fun post _ -> postRow dispatch model.Retrying post } }
    }
  | Posts.NotLoaded reason, _ ->
    Fragment() { Parts.Alert(tone = Parts.Bad, title = "The posts did not load.").Has(p () { reason }) }

[<SolidComponent>]
let private posts (dispatch: Shell.Msg -> unit) (model: Posts.Model) =
  let filter = Posts.Filtered >> Shell.Posts >> dispatch

  Fragment() {
    div (class' = "filter-bar toolbar") {
      Parts.tabs "Show" Posts.filters (fun () -> model.Filter) filter

      Parts
        .Button(
          kind = Parts.Primary,
          size = Parts.Small,
          disabled = model.Creating,
          onClick = fun _ -> dispatch (Shell.Posts Posts.NewDraftClicked)
        )
        .Has(Fragment() { "New draft" })
    }

    Show(when' = model.CreateFailure.IsSome) {
      Parts
        .Alert(tone = Parts.Bad, title = "The draft was not made.")
        .Has(p () { model.CreateFailure |> Option.defaultValue "" })
    }

    Show(when' = model.RetryFailure.IsSome) {
      Parts
        .Alert(tone = Parts.Bad, title = "The rebuild did not start.")
        .Has(p () { model.RetryFailure |> Option.defaultValue "" })
    }

    postList dispatch model
  }

let private subscriptionTone (subscription: Api.Subscription) =
  if subscription.IsActive then Parts.Good else Parts.Plain

let private countsText (model: Subscriptions.Model) =
  match Subscriptions.counts model with
  | Some counts -> $"{counts.Active} active and {counts.Inactive} inactive"
  | None -> "The counts come with the list."

[<SolidComponent>]
let private subscriptionsToolbar (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  let send = Shell.Subscriptions >> dispatch

  div (class' = "filter-bar toolbar") {
    p (class' = "muted", ariaLive = "polite") { countsText (model ()).Subscriptions }

    Parts
      .Search(
        label = "Search the subscriptions",
        placeholder = "Email address or name",
        value = (fun () -> (model ()).Subscriptions.Search),
        onInput = fun event -> send (Subscriptions.Searched((event.target :?> HTMLInputElement).value))
      )
      .View()

    Parts.tabs "Status" Subscriptions.filters (fun () -> (model ()).Subscriptions.Filter) (Subscriptions.Filtered >> send)
  }

[<SolidComponent>]
let private subscriptionRow (dispatch: Shell.Msg -> unit) (subscription: Api.Subscription) =
  article (id = subscription.Id, class' = "card card-body queue-row subscription-row") {
    div (class' = "row-top") {
      div (class' = "grow") {
        p (class' = "strong ellipsis") {
          subscription.Email
          " "
          Parts.badge (subscriptionTone subscription) (Subscriptions.statusText subscription)
        }

        p (class' = "muted small ellipsis") { subscription.Name |> Option.defaultValue "No name" }
      }

      Parts
        .Button(
          kind = Parts.Ghost,
          size = Parts.Small,
          onClick = fun _ -> dispatch (Shell.Subscriptions(Subscriptions.Opened subscription.Id))
        )
        .Has(Fragment() { "Details" })
    }

    p (class' = "muted small") { $"Subscribed: {Dates.dateText subscription.CreatedDate}" }
  }

[<SolidComponent>]
let private subscriptionList (dispatch: Shell.Msg -> unit) (model: Subscriptions.Model) =
  match model.Subscriptions, Subscriptions.shown model with
  | Subscriptions.Waiting, _ ->
    Fragment() {
      div (class' = "card card-body", ariaBusy = true) {
        Parts.skeleton "40%"
        Parts.skeleton "80%"
      }
    }
  | Subscriptions.Loaded _, [] ->
    Fragment() {
      Parts.emptyState
        Icon.Inbox
        (Subscriptions.emptyText model.Filter model.Search)
        (p () { "Each reader who subscribes to the newsletter comes here." })
    }
  | Subscriptions.Loaded _, subscriptions ->
    Fragment() {
      div (class' = "queue") {
        For(each = Array.ofList subscriptions) { yield fun subscription _ -> subscriptionRow dispatch subscription }
      }
    }
  | Subscriptions.NotLoaded reason, _ ->
    Fragment() { Parts.Alert(tone = Parts.Bad, title = "The subscriptions did not load.").Has(p () { reason }) }

[<SolidComponent>]
let private deliveryRow (delivery: Api.Delivery) =
  li (class' = "delivery") {
    Parts.badge Parts.Plain (Subscriptions.deliveryText delivery.Type)
    " "
    a (href = $"{site}/posts/{delivery.PostSlug}/", target = "_blank", rel = "noopener") { delivery.PostTitle }
    span (class' = "muted small") { $" on {Dates.dateText delivery.CreatedDate}" }
  }

[<SolidComponent>]
let private deliveryList (deliveries: Subscriptions.Deliveries) =
  match deliveries with
  | Subscriptions.DeliveriesWaiting ->
    Fragment() {
      div (ariaBusy = true) {
        Parts.skeleton "60%"
        Parts.skeleton "40%"
      }
    }
  | Subscriptions.DeliveriesLoaded [] -> Fragment() { p (class' = "muted") { "This subscription has no delivery." } }
  | Subscriptions.DeliveriesLoaded deliveries ->
    Fragment() { ul (class' = "deliveries") { For(each = Array.ofList deliveries) { yield fun delivery _ -> deliveryRow delivery } } }
  | Subscriptions.DeliveriesNotLoaded reason ->
    Fragment() { Parts.Alert(tone = Parts.Bad, title = "The deliveries did not load.").Has(p () { reason }) }

[<SolidComponent>]
let private subscriptionAction (dispatch: Shell.Msg -> unit) (subscriptionId: string) (sending: bool) (action: Subscriptions.Action) =
  let choose () =
    match action with
    | Subscriptions.Delete -> dispatch (Shell.Opened Shell.DeleteSubscriptionDialog)
    | Subscriptions.Cancel -> dispatch (Shell.Subscriptions(Subscriptions.Chosen(subscriptionId, action)))

  let kind =
    match action with
    | Subscriptions.Cancel -> Parts.Secondary
    | Subscriptions.Delete -> Parts.Ghost

  Parts
    .Button(kind = kind, size = Parts.Small, disabled = sending, onClick = fun _ -> choose ())
    .Has(Fragment() { Subscriptions.label action })

[<SolidComponent>]
let private subscriptionDetails (dispatch: Shell.Msg -> unit) (model: Subscriptions.Model) =
  match Subscriptions.tryOpened model, model.Sheet with
  | Some subscription, Some sheet ->
    Fragment() {
      dl (class' = "fields") {
        dt () { "Email" }
        dd () { subscription.Email }
        dt () { "Name" }
        dd () { subscription.Name |> Option.defaultValue "No name" }
        dt () { "Status" }
        dd () { Parts.badge (subscriptionTone subscription) (Subscriptions.statusText subscription) }
        dt () { "Subscribed" }
        dd () { Dates.dateText subscription.CreatedDate }

        Show(when' = subscription.CanceledDate.IsSome) {
          dt () { "Canceled" }
          dd () { subscription.CanceledDate |> Option.map Dates.dateText |> Option.defaultValue "" }
        }
      }

      Show(when' = (Subscriptions.failure sheet.State).IsSome) {
        Parts.Alert(tone = Parts.Bad).Has(p () { Subscriptions.failure sheet.State |> Option.defaultValue "" })
      }

      div (class' = "actions") {
        For(each = Array.ofList (Subscriptions.actions subscription)) {
          yield fun action _ -> subscriptionAction dispatch subscription.Id (Progress.isSending sheet.State) action
        }
      }

      h3 () { "Deliveries" }
      deliveryList sheet.Deliveries
    }
  | _ -> Fragment() { p (class' = "muted") { "This subscription is not in the list." } }

[<SolidComponent>]
let private subscriptionSheet (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  let close () = dispatch (Shell.Subscriptions Subscriptions.Closed)

  Parts.sheet
    "subscription-sheet"
    (fun () -> (model ()).Subscriptions.Sheet.IsSome)
    close
    "Subscription"
    (Fragment() {
      Parts.sheetHead "Subscription" close
      Parts.sheetBody (subscriptionDetails dispatch (model ()).Subscriptions)
    })

[<SolidComponent>]
let private deviceRow (dispatch: Shell.Msg -> unit) (removing: string list) (device: Api.Device) =
  article (id = device.Id, class' = "card card-body queue-row device-row") {
    div (class' = "row-top") {
      div (class' = "grow") {
        p (class' = "strong ellipsis") { device.Label }
        p (class' = "muted small") { $"{Notifications.addedText device} · {Notifications.lastSeenText device}" }
      }

      Parts
        .Button(
          kind = Parts.Ghost,
          size = Parts.Small,
          disabled = List.contains device.Id removing,
          onClick = fun _ -> dispatch (Shell.Notifications(Notifications.RemoveClicked device.Id))
        )
        .Has(Fragment() { "Remove" })
    }
  }

[<SolidComponent>]
let private deviceList (dispatch: Shell.Msg -> unit) (model: Notifications.Model) =
  match model.Devices with
  | Notifications.Waiting ->
    Fragment() {
      div (class' = "card card-body", ariaBusy = true) {
        Parts.skeleton "40%"
        Parts.skeleton "80%"
      }
    }
  | Notifications.Loaded [] -> Fragment() { p (class' = "muted") { Notifications.emptyText } }
  | Notifications.Loaded devices ->
    Fragment() {
      div (class' = "queue") {
        For(each = Array.ofList devices) { yield fun device _ -> deviceRow dispatch model.Removing device }
      }
    }
  | Notifications.NotLoaded reason ->
    Fragment() { Parts.Alert(tone = Parts.Bad, title = "The devices did not load.").Has(p () { reason }) }

[<SolidComponent>]
let private notificationsSection (dispatch: Shell.Msg -> unit) (model: Notifications.Model) =
  let section = Notifications.section model

  div (class' = "card card-body notifications") {
    h2 () { "Notifications" }
    p () { Notifications.sectionText section }

    Show(when' = (section = Notifications.Off)) {
      Parts
        .Button(
          kind = Parts.Primary,
          disabled = Notifications.isBusy model,
          onClick = fun _ -> dispatch (Shell.Notifications Notifications.TurnOnClicked)
        )
        .Has(Fragment() { "Turn on notifications on this device" })
    }

    Show(when' = (Notifications.registrationFailure model).IsSome) {
      Parts
        .Alert(tone = Parts.Bad, title = "This device did not register.")
        .Has(p () { Notifications.registrationFailure model |> Option.defaultValue "" })
    }

    h3 () { "Registered devices" }

    Show(when' = model.RemoveFailure.IsSome) {
      Parts
        .Alert(tone = Parts.Bad, title = "The device did not go away.")
        .Has(p () { model.RemoveFailure |> Option.defaultValue "" })
    }

    deviceList dispatch model
  }

[<SolidComponent>]
let private content (dispatch: Shell.Msg -> unit) (model: Shell.Model) =
  let route = Shell.Router >> dispatch

  match model.Router.Route with
  | Dashboard ->
    Fragment() {
      dashboard model.Dashboard
      rebuild dispatch model.Rebuild
    }
  | Settings ->
    Fragment() {
      notificationsSection dispatch model.Notifications

      div (class' = "card card-body settings") {
        dl (class' = "fields") {
          dt () { "API" }
          dd (class' = "mono") { Config.api () }
          dt () { "Admin" }
          dd (class' = "mono") { Config.origin () }
        }
      }
    }
  | ModerationQueue -> Fragment() { queue dispatch model.Queue }
  | PostComments slug ->
    Fragment() { postComments dispatch (model.PostComments |> Option.defaultValue (PostComments.init slug)) }
  | Posts -> Fragment() { posts dispatch model.Posts }
  | Commenters -> Fragment() { commenters dispatch model.Commenters }
  | Commenter commenterId ->
    Fragment() { commenterPage dispatch (model.Commenter |> Option.defaultValue (Commenter.init commenterId)) }
  | NotFound _ ->
    Fragment() {
      Parts.emptyState
        Icon.Alert
        "No page has this address."
        (p () {
          "Go to the "
          routeLink route Dashboard "dashboard"
          "."
        })
    }
  | Subscriptions -> Fragment() { subscriptionList dispatch model.Subscriptions }
  // The editor has its own page.
  | Post _ -> Fragment() { "" }
  | Series as route ->
    Fragment() {
      Parts.emptyState (Icon.ofRoute route) "This page is empty." (p () { "Its content comes in a later version." })
    }

[<SolidComponent>]
let private deleteDialog (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  let close () =
    match (model ()).Overlay with
    | Some(Shell.DeleteDialog _ as overlay) -> dispatch (Shell.Closed overlay)
    | Some Shell.DeleteCommenterDialog
    | Some Shell.DeleteSubscriptionDialog
    | Some Shell.MoreSheet
    | Some Shell.SignOutDialog
    | None -> ()

  let confirm () = Shell.deleteChosen (model ()) |> Option.iter dispatch

  Parts.modalDialog
    "delete-comment"
    (fun () -> (Shell.deleteChosen (model ())).IsSome)
    close
    "Delete the comment?"
    (p () {
      "The comment and its history go away permanently. To hide the comment from readers and keep it, reject it."
    })
    (Fragment() {
      Parts.Button(onClick = fun _ -> close ()).Has(Fragment() { "Cancel" })
      Parts.Button(kind = Parts.Danger, onClick = fun _ -> confirm ()).Has(Fragment() { "Delete" })
    })

[<SolidComponent>]
let private deleteCommenterDialog (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  let close () = dispatch (Shell.Closed Shell.DeleteCommenterDialog)
  let confirm () = Shell.deleteCommenterChosen (model ()) |> Option.iter dispatch

  Parts.modalDialog
    "delete-commenter"
    (fun () -> (Shell.deleteCommenterChosen (model ())).IsSome)
    close
    "Delete the commenter?"
    (p () {
      "The commenter and each comment of the commenter go away permanently. The email address can verify again later, as a new commenter. To keep the commenter from commenting, ban the commenter."
    })
    (Fragment() {
      Parts.Button(onClick = fun _ -> close ()).Has(Fragment() { "Cancel" })
      Parts.Button(kind = Parts.Danger, onClick = fun _ -> confirm ()).Has(Fragment() { "Delete" })
    })

[<SolidComponent>]
let private deleteSubscriptionDialog (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  let close () = dispatch (Shell.Closed Shell.DeleteSubscriptionDialog)
  let confirm () = Shell.deleteSubscriptionChosen (model ()) |> Option.iter dispatch

  Parts.modalDialog
    "delete-subscription"
    (fun () -> (Shell.deleteSubscriptionChosen (model ())).IsSome)
    close
    "Delete the subscription?"
    (p () {
      "The subscription and each delivery record of the subscription go away permanently. The reader gets no email. To keep the record, cancel the subscription."
    })
    (Fragment() {
      Parts.Button(onClick = fun _ -> close ()).Has(Fragment() { "Keep" })
      Parts.Button(kind = Parts.Danger, onClick = fun _ -> confirm ()).Has(Fragment() { "Delete" })
    })

[<Emit "document.getElementById($0)?.scrollIntoView({ block: 'center' })">]
let private scrollToId (id: string) : unit = jsNative

[<SolidComponent>]
let view (signIn: SignIn) (push: Push) =
  let model, dispatch =
    Mvu.run (Shell.init (window.location.pathname + window.location.hash)) Shell.update (perform signIn push)

  let fragmentToShow () =
    let model = model ()

    let loaded =
      match model.Router.Route with
      | ModerationQueue -> Queue.count model.Queue <> None
      | PostComments _ -> model.PostComments |> Option.exists PostComments.isLoaded
      | _ -> false

    if loaded then model.Router.Fragment else ""

  createEffect (fun () ->
    match fragmentToShow () with
    | "" -> ()
    | id -> scrollToId id)

  dispatch (Shell.Notifications(Notifications.Detected(Notifications.detect push)))
  push.onSubscriptionChange (fun () -> dispatch (Shell.Notifications Notifications.SubscriptionChanged))
  push.onNewComment (fun () -> dispatch Shell.NewCommentAnnounced)
  push.onOpenRequest (Shell.OpenRequested >> dispatch)

  let route = Shell.Router >> dispatch

  window.addEventListener ("popstate", fun _ -> route (Router.UrlChanged(window.location.pathname + window.location.hash)))

  let current () = (model ()).Router.Route
  let showsNavigation () = Router.showsNavigation (current ())
  let queueCount () = Queue.count (model ()).Queue

  let name () =
    match (model ()).Session.Status with
    | Session.Starting -> "Author"
    | _ -> signIn.account () |> Option.defaultValue "Author"

  let isOpen (overlay: Shell.Overlay) () = (model ()).Overlay = Some overlay
  let opened (overlay: Shell.Overlay) () = dispatch (Shell.Opened overlay)
  let closed (overlay: Shell.Overlay) () = dispatch (Shell.Closed overlay)

  div (class' = (if showsNavigation () then "shell" else "shell is-bare paper")) {
    Show(when' = showsNavigation ()) {
      aside (class' = "side") {
        brand route

        nav (class' = "side-nav", ariaLabel = "Admin") {
          For(each = Array.ofList Router.navigation) { yield fun item _ -> sideLink route current queueCount item }
        }

        div (class' = "side-foot") {
          a (class' = "side-link", href = site, target = "_blank", rel = "noopener", title = "Open twopoint.dev") {
            Icon.view Icon.External
            span (class' = "nav-label") { "Open twopoint.dev" }
          }

          button (
            type' = "button",
            class' = "account",
            title = "Account",
            ariaHasPopup = "menu",
            popovertarget = "account-menu"
          ) {
            Parts.avatar (name ())

            span (class' = "nav-label grow") {
              span (class' = "strong small block ellipsis") { name () }
              span (class' = "xs muted block") { "Entra ID" }
            }

            span (class' = "nav-label muted") { Icon.view Icon.More }
          }

          accountMenu "account-menu" { Side = Menu.Above; Edge = Menu.Start } name (opened Shell.SignOutDialog)
        }
      }
    }

    div (class' = "main") {
      Show(when' = showsNavigation ()) {
        header (class' = "top") {
          p (class' = "crumb") {
            "twopoint admin"
            span (class' = "sep") { "/" }
            span (class' = "here") { Router.title (current ()) }
          }

          span (class' = "phone-only grow") { brand route }

          span (class' = "phone-only") {
            button (
              type' = "button",
              class' = "avatar-button",
              ariaLabel = "Account",
              ariaHasPopup = "menu",
              popovertarget = "phone-account-menu"
            ) {
              Parts.avatar (name ())
            }

            accountMenu "phone-account-menu" { Side = Menu.Below; Edge = Menu.End } name (opened Shell.SignOutDialog)
          }
        }
      }

      // The editor stays outside the page, so a typed letter does not replace the editor.
      Show(when' = not (showsNavigation ())) {
        EditorView.page dispatch model (follow route Posts) (fun () -> Router.title (current ()))
      }

      Show(when' = showsNavigation ()) {
        main (class' = "screen") {
          Banner.view dispatch (model ()).Session
          div (class' = "page-head") { h1 () { Router.title (current ()) } }

          // The search input stays outside the page, so a typed letter does not replace the input.
          Show(when' = (current () = Subscriptions)) { subscriptionsToolbar dispatch model }

          content dispatch (model ())
        }
      }
    }

    Show(when' = showsNavigation ()) {
      nav (class' = "tab-bar", ariaLabel = "Tabs") {
        For(each = Array.ofList Router.tabs) {
          yield fun tab _ -> tabLink route current queueCount (opened Shell.MoreSheet) tab
        }
      }
    }

    Parts.sheet
      "more-sheet"
      (isOpen Shell.MoreSheet)
      (closed Shell.MoreSheet)
      "More"
      (Parts.sheetBody (
        Fragment() {
          For(each = Array.ofList Router.morePages) {
            yield
              fun item _ ->
                a (class' = "more-link", href = Route.path item, onClick = follow route item) {
                  Icon.view (Icon.ofRoute item)
                  Router.title item
                }
          }

          a (class' = "more-link", href = site, target = "_blank", rel = "noopener") {
            Icon.view Icon.External
            "Open twopoint.dev"
          }

          button (type' = "button", class' = "more-link", onClick = fun _ -> opened Shell.SignOutDialog ()) {
            Icon.view Icon.SignOut
            "Sign out"
          }
        }
      ))

    Parts.modalDialog
      "sign-out"
      (isOpen Shell.SignOutDialog)
      (closed Shell.SignOutDialog)
      "Sign out?"
      (p () { "Entra signs you out on this browser. To use the admin again, you must sign in again." })
      (Fragment() {
        Parts.Button(onClick = fun _ -> closed Shell.SignOutDialog ()).Has(Fragment() { "Cancel" })

        Parts
          .Button(kind = Parts.Danger, onClick = fun _ -> dispatch (Shell.Session Session.SignOutClicked))
          .Has(Fragment() { "Sign out" })
      })

    deleteDialog dispatch model
    deleteCommenterDialog dispatch model
    deleteSubscriptionDialog dispatch model
    subscriptionSheet dispatch model
  }
