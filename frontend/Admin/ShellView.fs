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
  | Shell.LoadQueue -> Shell.Queue(Queue.Answered(Error Api.Unanswered))
  | Shell.Moderate(comment, action) -> Shell.Queue(Queue.Done(comment.Comment.Id, action, Error Api.Unanswered))
  | Shell.RebuildSite -> Shell.Rebuild(Rebuild.Answered(Error Api.Unanswered))

let private moderate (token: string) (comment: Api.QueuedComment) (action: Queue.Action) =
  match action with
  | Queue.Approve -> Api.changeApproval (Config.api ()) token comment Api.Approved
  | Queue.Reject -> Api.changeApproval (Config.api ()) token comment Api.Rejected
  | Queue.Delete -> Api.deleteComment (Config.api ()) token comment

let private answer (call: Shell.Call) (token: string) : JS.Promise<Shell.Msg> =
  match call with
  | Shell.LoadDashboard -> Api.getPosts (Config.api ()) token |> Promise.map (Dashboard.Answered >> Shell.Dashboard)
  | Shell.LoadQueue ->
    Api.getModerationQueue (Config.api ()) token
    |> Promise.map (Queue.Answered >> Shell.Queue)
  | Shell.Moderate(comment, action) ->
    moderate token comment action
    |> Promise.map (fun result -> Shell.Queue(Queue.Done(comment.Comment.Id, action, result)))
  | Shell.RebuildSite -> Api.rebuildSite (Config.api ()) token |> Promise.map (Rebuild.Answered >> Shell.Rebuild)

let private send (signIn: SignIn) (dispatch: Shell.Msg -> unit) (call: Shell.Call) =
  signIn.token ()
  |> Promise.bind (fun token ->
    match token with
    | Some token -> answer call token
    | None -> Promise.lift (Shell.Session(Session.InteractionRequired call)))
  |> Promise.catch (fun error ->
    console.error ("admin: no token for the API call.", error)
    fail call)
  |> Promise.map dispatch
  |> Promise.start

let private signInWith (signIn: SignIn) (effect: Session.Effect<Shell.Call>) (dispatch: Shell.Msg -> unit) =
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
  | Session.Retry calls -> calls |> List.iter (send signIn dispatch)
  | Session.SignOut ->
    signIn.signOut ()
    |> Promise.catch (report "the Entra sign-out did not open." Session.SignOutFailed)
    |> Promise.start

let private perform (signIn: SignIn) (effect: Shell.Effect) (dispatch: Shell.Msg -> unit) =
  match effect with
  | Shell.Navigate(Router.PushUrl path) -> history.pushState (null, "", path)
  | Shell.SignIn effect -> signInWith signIn effect dispatch
  | Shell.Send calls -> calls |> List.iter (send signIn dispatch)

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

let private statusTone (status: Queue.CommenterStatus) =
  match status with
  | Queue.New -> Parts.Plain
  | Queue.Trusted -> Parts.Good
  | Queue.Banned -> Parts.Bad

[<SolidComponent>]
let private queueRow (dispatch: Shell.Msg -> unit) (row: Queue.Row) =
  let comment = row.Comment.Comment
  let status = Queue.commenterStatus row
  let choose action = dispatch (Shell.Queue(Queue.Chosen(comment.Id, action)))

  article (id = comment.Id, class' = "card card-body queue-row") {
    div (class' = "queue-head") {
      Parts.avatar comment.Commenter.Name

      div (class' = "grow") {
        p (class' = "strong") {
          comment.Commenter.Name
          " "
          Parts.badge (statusTone status) (Queue.statusText status)
        }

        p (class' = "muted small ellipsis") { comment.Commenter.Email }
      }
    }

    p (class' = "muted small") { $"On {row.Comment.PostTitle}" }
    p (class' = "queue-text") { comment.Content }

    Show(when' = (Queue.reason row).IsSome) {
      Parts.Alert(tone = Parts.Bad).Has(p () { Queue.reason row |> Option.defaultValue "" })
    }

    div (class' = "actions") {
      Parts
        .Button(kind = Parts.Accent, size = Parts.Small, disabled = Queue.isSending row, onClick = fun _ -> choose Queue.Approve)
        .Has(Fragment() { "Approve" })

      Parts
        .Button(size = Parts.Small, disabled = Queue.isSending row, onClick = fun _ -> choose Queue.Reject)
        .Has(Fragment() { "Reject" })

      Parts
        .Button(
          kind = Parts.Ghost,
          size = Parts.Small,
          disabled = Queue.isSending row,
          onClick = fun _ -> dispatch (Shell.Opened(Shell.DeleteDialog comment.Id))
        )
        .Has(Fragment() { "Delete" })
    }
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
  | Posts
  | Post _
  | Commenters
  | Series
  | Subscriptions as route ->
    Fragment() {
      Parts.emptyState (Icon.ofRoute route) "This page is empty." (p () { "Its content comes in a later version." })
    }

[<SolidComponent>]
let private bannerWith (text: string) (action: string) (disabled: bool) (click: unit -> unit) =
  Parts
    .Alert(tone = Parts.Warn, extra = "banner")
    .Has(
      Fragment() {
        p () { text }

        Parts
          .Button(kind = Parts.Primary, size = Parts.Small, disabled = disabled, onClick = fun _ -> click ())
          .Has(Fragment() { action })
      }
    )

[<SolidComponent>]
let private banner (dispatch: Shell.Msg -> unit) (model: Session.Model<Shell.Call>) =
  let session = Shell.Session >> dispatch

  match model.Status with
  | Session.Broken ->
    Fragment() { bannerWith "The sign-in did not complete." "Sign in" false (fun () -> session Session.SignInClicked) }
  | Session.Refused ->
    Fragment() {
      bannerWith (Session.bannerText model) "Use another account" false (fun () -> session Session.SignInClicked)
    }
  | _ ->
    Fragment() {
      Show(when' = Session.showsBanner model) {
        bannerWith (Session.bannerText model) "Sign in again" (model.Status = Session.Renewing) (fun () ->
          session Session.SignInAgainClicked)
      }
    }

[<SolidComponent>]
let private deleteDialog (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  let target () =
    match (model ()).Overlay with
    | Some(Shell.DeleteDialog commentId) -> Queue.tryFind commentId (model ()).Queue
    | Some Shell.MoreSheet
    | Some Shell.SignOutDialog
    | None -> None

  let close () =
    match (model ()).Overlay with
    | Some(Shell.DeleteDialog _ as overlay) -> dispatch (Shell.Closed overlay)
    | Some Shell.MoreSheet
    | Some Shell.SignOutDialog
    | None -> ()

  let confirm () =
    target ()
    |> Option.iter (fun row -> dispatch (Shell.Queue(Queue.Chosen(row.Comment.Comment.Id, Queue.Delete))))

  Parts.modalDialog
    "delete-comment"
    (fun () -> (target ()).IsSome)
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
let view (signIn: SignIn) =
  let model, dispatch =
    Mvu.run (Shell.init window.location.pathname) Shell.update (perform signIn)

  let route = Shell.Router >> dispatch

  window.addEventListener ("popstate", fun _ -> route (Router.UrlChanged window.location.pathname))

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

      Show(when' = not (showsNavigation ())) {
        header (class' = "bar") {
          a (class' = "back", href = Route.path Posts, onClick = follow route Posts) {
            Icon.view Icon.Back
            "Posts"
          }
        }
      }

      main (class' = "screen") {
        banner dispatch (model ()).Session
        div (class' = "page-head") { h1 () { Router.title (current ()) } }
        content dispatch (model ())
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
  }
