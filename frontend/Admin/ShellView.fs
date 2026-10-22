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

let private perform (signIn: SignIn) (effect: Shell.Effect) (dispatch: Shell.Msg -> unit) =
  match effect with
  | Shell.Navigate(Router.PushUrl path) -> history.pushState (null, "", path)
  | Shell.SignIn effect -> signInWith signIn effect dispatch
  | Shell.Send call -> send signIn dispatch call

let private follow (dispatch: Router.Msg -> unit) (path: string) (event: MouseEvent) =
  if event.button = 0. && not (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) then
    event.preventDefault ()
    dispatch (Router.LinkFollowed path)

[<SolidComponent>]
let private link (dispatch: Router.Msg -> unit) (route: Route) (current: Route) =
  a (
    href = Route.path route,
    ariaCurrent = (if Router.isCurrent route current then "page" else null),
    onClick = follow dispatch (Route.path route)
  ) {
    Router.title route
  }

[<SolidComponent>]
let private routeLink (dispatch: Router.Msg -> unit) (route: Route) (text: string) =
  a (href = Route.path route, onClick = follow dispatch (Route.path route)) { text }

[<SolidComponent>]
let private dashboard (model: Dashboard.Model) =
  match model with
  | Dashboard.Waiting -> Fragment() { p (class' = "empty") { "The admin loads the dashboard." } }
  | Dashboard.Loaded summary ->
    Fragment() {
      dl (class' = "summary") {
        dt () { "Posts" }
        dd () { string summary.Posts }
        dt () { "New comments" }
        dd () { string summary.NewComments }
      }
    }
  | Dashboard.NotLoaded reason -> Fragment() { p (class' = "problem") { reason } }

[<SolidComponent>]
let private content (dispatch: Router.Msg -> unit) (model: Shell.Model) =
  match model.Router.Route with
  | Dashboard -> dashboard model.Dashboard
  | Settings ->
    Fragment() {
      dl (class' = "settings") {
        dt () { "API" }
        dd () { Config.api () }
        dt () { "Admin" }
        dd () { Config.origin () }
      }
    }
  | NotFound _ ->
    Fragment() {
      p () {
        "No page has this address. Go to the "
        routeLink dispatch Dashboard "dashboard"
        "."
      }
    }
  | Posts
  | Post _
  | ModerationQueue
  | Commenters
  | Series
  | Subscriptions -> Fragment() { p (class' = "empty") { "This page is empty." } }

[<SolidComponent>]
let private banner (dispatch: Shell.Msg -> unit) (model: Session.Model<Shell.Call>) =
  match model.Status with
  | Session.Broken ->
    Fragment() {
      div (class' = "banner", role = "alert") {
        p () { "The sign-in did not complete." }
        button (type' = "button", onClick = fun _ -> dispatch (Shell.Session Session.SignInClicked)) { "Sign in" }
      }
    }
  | Session.Refused ->
    Fragment() {
      div (class' = "banner", role = "alert") {
        p () { Session.bannerText model }

        button (type' = "button", onClick = fun _ -> dispatch (Shell.Session Session.SignInClicked)) {
          "Use another account"
        }
      }
    }
  | _ ->
    Fragment() {
      Show(when' = Session.showsBanner model) {
        div (class' = "banner", role = "alert") {
          p () { Session.bannerText model }

          button (
            type' = "button",
            disabled = (model.Status = Session.Renewing),
            onClick = fun _ -> dispatch (Shell.Session Session.SignInAgainClicked)
          ) {
            "Sign in again"
          }
        }
      }
    }

[<SolidComponent>]
let view (signIn: SignIn) =
  let model, dispatch =
    Mvu.run (Shell.init window.location.pathname) Shell.update (perform signIn)

  let route = Shell.Router >> dispatch

  window.addEventListener ("popstate", fun _ -> route (Router.UrlChanged window.location.pathname))

  let showsNavigation () = Router.showsNavigation (model ()).Router.Route

  div (class' = (if showsNavigation () then "shell" else "shell is-bare")) {
    Show(when' = showsNavigation ()) {
      nav (class' = "navigation", ariaLabel = "Admin") {
        p (class' = "brand") { "TwoPoint Admin" }

        ul () {
          For(each = Array.ofList Router.navigation) {
            yield fun item _ -> li () { link route item (model ()).Router.Route }
          }
        }
      }
    }

    Show(when' = not (showsNavigation ())) {
      header (class' = "bar") { routeLink route Posts "Posts" }
    }

    main (class' = "screen") {
      banner dispatch (model ()).Session
      h1 () { Router.title (model ()).Router.Route }
      content route (model ())
    }
  }
