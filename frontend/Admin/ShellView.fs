[<RequireQualifiedAccess>]
module TwoPoint.Admin.ShellView

open TwoPoint.Site.Widgets

open Browser
open Browser.Types
open Oxpecker.Solid
open Oxpecker.Solid.Aria

let private perform (effect: Router.Effect) (_: Router.Msg -> unit) =
  match effect with
  | Router.PushUrl path -> history.pushState (null, "", path)

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
let private content (dispatch: Router.Msg -> unit) (route: Route) =
  match route with
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
  | Dashboard
  | Posts
  | Post _
  | ModerationQueue
  | Commenters
  | Series
  | Subscriptions -> Fragment() { p (class' = "empty") { "This page is empty." } }

[<SolidComponent>]
let view () =
  let model, dispatch =
    Mvu.run (Router.init window.location.pathname, None) Router.update perform

  window.addEventListener ("popstate", fun _ -> dispatch (Router.UrlChanged window.location.pathname))

  let showsNavigation () = Router.showsNavigation (model ()).Route

  div (class' = (if showsNavigation () then "shell" else "shell is-bare")) {
    Show(when' = showsNavigation ()) {
      nav (class' = "navigation", ariaLabel = "Admin") {
        p (class' = "brand") { "TwoPoint Admin" }

        ul () {
          For(each = Array.ofList Router.navigation) {
            yield fun route _ -> li () { link dispatch route (model ()).Route }
          }
        }
      }
    }

    Show(when' = not (showsNavigation ())) {
      header (class' = "bar") { routeLink dispatch Posts "Posts" }
    }

    main (class' = "screen") {
      h1 () { Router.title (model ()).Route }
      content dispatch (model ()).Route
    }
  }
