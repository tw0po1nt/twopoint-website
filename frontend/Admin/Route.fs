namespace TwoPoint.Admin

open System

[<RequireQualifiedAccess>]
type PostFilter =
  | All
  | Drafts
  | PendingEdits
  | DeliveriesToStart

[<RequireQualifiedAccess>]
type SubscriptionFilter =
  | All
  | Active
  | Inactive

type Route =
  | Dashboard
  | Posts of PostFilter
  | Post of slug: string
  | PostComments of slug: string
  | ModerationQueue
  | Commenters
  | Commenter of id: string
  | Series
  | Subscriptions of SubscriptionFilter
  | Settings
  | NotFound of path: string

[<RequireQualifiedAccess>]
module PostFilter =

  let all = [ PostFilter.All; PostFilter.Drafts; PostFilter.PendingEdits; PostFilter.DeliveriesToStart ]

  /// "All" has no query string.
  let value (filter: PostFilter) =
    match filter with
    | PostFilter.All -> None
    | PostFilter.Drafts -> Some "drafts"
    | PostFilter.PendingEdits -> Some "pending-edits"
    | PostFilter.DeliveriesToStart -> Some "deliveries-to-start"

[<RequireQualifiedAccess>]
module SubscriptionFilter =

  let all = [ SubscriptionFilter.All; SubscriptionFilter.Active; SubscriptionFilter.Inactive ]

  /// "All" has no query string.
  let value (filter: SubscriptionFilter) =
    match filter with
    | SubscriptionFilter.All -> None
    | SubscriptionFilter.Active -> Some "active"
    | SubscriptionFilter.Inactive -> Some "inactive"

[<RequireQualifiedAccess>]
module Route =

  /// Splits the text at the first separator. With no separator, the second part is empty.
  let split (separator: char) (text: string) =
    match text.IndexOf separator with
    | -1 -> text, ""
    | index -> text.Substring(0, index), text.Substring(index + 1)

  let private filterValue (query: string) =
    query.Split '&'
    |> Array.tryPick (fun pair ->
      match split '=' pair with
      | "filter", value -> Some(Uri.UnescapeDataString value)
      | _ -> None)

  let private filterOf (filters: 'Filter list) (value: 'Filter -> string option) (query: string) =
    let wanted = filterValue query
    filters |> List.tryFind (fun filter -> value filter = wanted)

  let private query (value: string option) =
    match value with
    | Some value -> $"?filter={value}"
    | None -> ""

  /// Reads a path of the form `path?query`, with no fragment.
  let parse (path: string) =
    let pagePath, query = split '?' path

    let filtered (route: 'Filter -> Route) (filter: 'Filter option) =
      match filter with
      | Some filter -> route filter
      | None -> NotFound path

    match pagePath.Split '/' |> Array.filter (fun segment -> segment <> "") |> List.ofArray with
    | [] -> Dashboard
    | [ "posts" ] -> filterOf PostFilter.all PostFilter.value query |> filtered Posts
    | [ "posts"; slug ] -> Post(Uri.UnescapeDataString slug)
    | [ "posts"; slug; "comments" ] -> PostComments(Uri.UnescapeDataString slug)
    | [ "comments" ] -> ModerationQueue
    | [ "commenters" ] -> Commenters
    | [ "commenters"; id ] -> Commenter(Uri.UnescapeDataString id)
    | [ "series" ] -> Series
    | [ "subscriptions" ] -> filterOf SubscriptionFilter.all SubscriptionFilter.value query |> filtered Subscriptions
    | [ "settings" ] -> Settings
    | _ -> NotFound path

  let path (route: Route) =
    match route with
    | Dashboard -> "/"
    | Posts filter -> "/posts" + query (PostFilter.value filter)
    | Post slug -> $"/posts/{Uri.EscapeDataString slug}"
    | PostComments slug -> $"/posts/{Uri.EscapeDataString slug}/comments"
    | ModerationQueue -> "/comments"
    | Commenters -> "/commenters"
    | Commenter id -> $"/commenters/{Uri.EscapeDataString id}"
    | Series -> "/series"
    | Subscriptions filter -> "/subscriptions" + query (SubscriptionFilter.value filter)
    | Settings -> "/settings"
    | NotFound path -> path

  /// Ignores the filter of a page.
  let isSamePage (route: Route) (other: Route) =
    match route, other with
    | Posts _, Posts _
    | Subscriptions _, Subscriptions _ -> true
    | _ -> route = other

  let isPosts (route: Route) =
    match route with
    | Posts _ -> true
    | _ -> false

  let isSubscriptions (route: Route) =
    match route with
    | Subscriptions _ -> true
    | _ -> false
