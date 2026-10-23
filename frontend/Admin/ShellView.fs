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

let private answer (call: Shell.Call) (token: string) : JS.Promise<Shell.Msg> =
  match call with
  | Shell.LoadDashboard -> Api.getPosts (Config.api ()) token |> Promise.map (Dashboard.Answered >> Shell.Dashboard)

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
  | Shell.Send call -> send signIn dispatch call

let private site = "https://twopoint.dev"

let private follow (dispatch: Router.Msg -> unit) (path: string) (event: MouseEvent) =
  if event.button = 0. && not (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) then
    event.preventDefault ()
    dispatch (Router.LinkFollowed path)

let private currentMark (isCurrent: bool) = if isCurrent then "page" else null

[<SolidComponent>]
let private routeLink (dispatch: Router.Msg -> unit) (route: Route) (text: string) =
  a (href = Route.path route, onClick = follow dispatch (Route.path route)) { text }

[<SolidComponent>]
let private brand (dispatch: Router.Msg -> unit) =
  a (
    class' = "brand",
    href = Route.path Dashboard,
    ariaLabel = "twopoint admin",
    onClick = follow dispatch (Route.path Dashboard)
  ) {
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
    onClick = follow dispatch (Route.path item)
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
        onClick = follow dispatch (Route.path item)
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
let private content (dispatch: Router.Msg -> unit) (model: Shell.Model) =
  match model.Router.Route with
  | Dashboard -> dashboard model.Dashboard
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
  | NotFound _ ->
    Fragment() {
      Parts.emptyState
        Icon.Alert
        "No page has this address."
        (p () {
          "Go to the "
          routeLink dispatch Dashboard "dashboard"
          "."
        })
    }
  | Posts
  | Post _
  | ModerationQueue
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
let view (signIn: SignIn) =
  let model, dispatch =
    Mvu.run (Shell.init window.location.pathname) Shell.update (perform signIn)

  let route = Shell.Router >> dispatch

  window.addEventListener ("popstate", fun _ -> route (Router.UrlChanged window.location.pathname))

  let current () = (model ()).Router.Route
  let showsNavigation () = Router.showsNavigation (current ())
  let queue () = Dashboard.newComments (model ()).Dashboard

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
          For(each = Array.ofList Router.navigation) { yield fun item _ -> sideLink route current queue item }
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
          a (class' = "back", href = Route.path Posts, onClick = follow route (Route.path Posts)) {
            Icon.view Icon.Back
            "Posts"
          }
        }
      }

      main (class' = "screen") {
        banner dispatch (model ()).Session
        div (class' = "page-head") { h1 () { Router.title (current ()) } }
        content route (model ())
      }
    }

    Show(when' = showsNavigation ()) {
      nav (class' = "tab-bar", ariaLabel = "Tabs") {
        For(each = Array.ofList Router.tabs) {
          yield fun tab _ -> tabLink route current queue (opened Shell.MoreSheet) tab
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
                a (class' = "more-link", href = Route.path item, onClick = follow route (Route.path item)) {
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
  }
