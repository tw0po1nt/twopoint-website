[<RequireQualifiedAccess>]
module TwoPoint.Admin.Router

type Model = { Route: Route; Fragment: string }

type Msg =
  | LinkFollowed of path: string
  | UrlChanged of path: string

type Effect = PushUrl of path: string

let init (path: string) =
  let path, fragment = Route.split '#' path
  { Route = Route.parse path; Fragment = fragment }

let update (msg: Msg) (model: Model) : Model * Effect option =
  match msg with
  | LinkFollowed path ->
    let path, fragment = Route.split '#' path
    let route = Route.parse path

    if route = model.Route && fragment = model.Fragment then
      model, None
    else
      let hash = if fragment = "" then "" else $"#{fragment}"
      { Route = route; Fragment = fragment }, Some(PushUrl(Route.path route + hash))
  | UrlChanged path -> init path, None

let navigation =
  [ Dashboard
    Posts PostFilter.All
    ModerationQueue
    Commenters
    Series
    Subscriptions SubscriptionFilter.All
    Settings ]

let title (route: Route) =
  match route with
  | Dashboard -> "Dashboard"
  | Posts _ -> "Posts"
  | Post slug -> slug
  | PostComments slug -> $"Comments on {slug}"
  | ModerationQueue -> "Moderation queue"
  | Commenters -> "Commenters"
  | Commenter _ -> "Commenter"
  | Series -> "Series"
  | Subscriptions _ -> "Subscriptions"
  | Settings -> "Settings"
  | NotFound _ -> "Page not found"

let showsNavigation (route: Route) =
  match route with
  | Post _ -> false
  | Dashboard
  | Posts _
  | PostComments _
  | ModerationQueue
  | Commenters
  | Commenter _
  | Series
  | Subscriptions _
  | Settings
  | NotFound _ -> true

let isCurrent (item: Route) (route: Route) =
  match route with
  | Post _
  | PostComments _ -> Route.isPosts item
  | Commenter _ -> item = Commenters
  | _ -> Route.isSamePage item route

/// A tab of the tab bar of a phone.
type Tab =
  | Page of Route
  /// Opens a sheet with the pages that have no tab.
  | More

let tabs =
  [ Page Dashboard
    Page(Posts PostFilter.All)
    Page ModerationQueue
    Page(Subscriptions SubscriptionFilter.All)
    More ]

let morePages = [ Commenters; Series; Settings ]

let tabTitle (tab: Tab) =
  match tab with
  | Page Dashboard -> "Home"
  | Page ModerationQueue -> "Queue"
  | Page(Subscriptions _) -> "Readers"
  | Page route -> title route
  | More -> "More"

let isCurrentTab (tab: Tab) (route: Route) =
  match tab with
  | Page item -> isCurrent item route
  | More -> morePages |> List.exists (fun item -> isCurrent item route)
