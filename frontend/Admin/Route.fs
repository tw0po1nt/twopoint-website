namespace TwoPoint.Admin

open System

type Route =
  | Dashboard
  | Posts
  | Post of slug: string
  | PostComments of slug: string
  | ModerationQueue
  | Commenters
  | Commenter of id: string
  | Series
  | Subscriptions
  | Settings
  | NotFound of path: string

[<RequireQualifiedAccess>]
module Route =

  let parse (path: string) =
    match path.Split '/' |> Array.filter (fun segment -> segment <> "") |> List.ofArray with
    | [] -> Dashboard
    | [ "posts" ] -> Posts
    | [ "posts"; slug ] -> Post(Uri.UnescapeDataString slug)
    | [ "posts"; slug; "comments" ] -> PostComments(Uri.UnescapeDataString slug)
    | [ "comments" ] -> ModerationQueue
    | [ "commenters" ] -> Commenters
    | [ "commenters"; id ] -> Commenter(Uri.UnescapeDataString id)
    | [ "series" ] -> Series
    | [ "subscriptions" ] -> Subscriptions
    | [ "settings" ] -> Settings
    | _ -> NotFound path

  let path (route: Route) =
    match route with
    | Dashboard -> "/"
    | Posts -> "/posts"
    | Post slug -> $"/posts/{Uri.EscapeDataString slug}"
    | PostComments slug -> $"/posts/{Uri.EscapeDataString slug}/comments"
    | ModerationQueue -> "/comments"
    | Commenters -> "/commenters"
    | Commenter id -> $"/commenters/{Uri.EscapeDataString id}"
    | Series -> "/series"
    | Subscriptions -> "/subscriptions"
    | Settings -> "/settings"
    | NotFound path -> path
