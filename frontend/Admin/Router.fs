[<RequireQualifiedAccess>]
module TwoPoint.Admin.Router

type Model = { Route: Route }

type Msg =
  | LinkFollowed of path: string
  | UrlChanged of path: string

type Effect = PushUrl of path: string

let init (path: string) = { Route = Route.parse path }

let update (msg: Msg) (model: Model) : Model * Effect option =
  match msg with
  | LinkFollowed path ->
    let route = Route.parse path

    if route = model.Route then
      model, None
    else
      { Route = route }, Some(PushUrl(Route.path route))
  | UrlChanged path -> { Route = Route.parse path }, None

let navigation =
  [ Dashboard; Posts; ModerationQueue; Commenters; Series; Subscriptions; Settings ]

let title (route: Route) =
  match route with
  | Dashboard -> "Dashboard"
  | Posts -> "Posts"
  | Post slug -> slug
  | ModerationQueue -> "Moderation queue"
  | Commenters -> "Commenters"
  | Series -> "Series"
  | Subscriptions -> "Subscriptions"
  | Settings -> "Settings"
  | NotFound _ -> "Page not found"

let showsNavigation (route: Route) =
  match route with
  | Post _ -> false
  | Dashboard
  | Posts
  | ModerationQueue
  | Commenters
  | Series
  | Subscriptions
  | Settings
  | NotFound _ -> true

let isCurrent (item: Route) (route: Route) =
  match route with
  | Post _ -> item = Posts
  | _ -> item = route

/// A tab of the tab bar of a phone.
type Tab =
  | Page of Route
  /// Opens a sheet with the pages that have no tab.
  | More

let tabs = [ Page Dashboard; Page Posts; Page ModerationQueue; Page Subscriptions; More ]

let morePages = [ Commenters; Series; Settings ]

let tabTitle (tab: Tab) =
  match tab with
  | Page Dashboard -> "Home"
  | Page ModerationQueue -> "Queue"
  | Page Subscriptions -> "Readers"
  | Page route -> title route
  | More -> "More"

let isCurrentTab (tab: Tab) (route: Route) =
  match tab with
  | Page item -> isCurrent item route
  | More -> morePages |> List.exists (fun item -> isCurrent item route)
