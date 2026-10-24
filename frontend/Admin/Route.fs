namespace TwoPoint.Admin

open System

type Route =
  | Dashboard
  | Posts
  | Post of slug: string
  | PostComments of slug: string
  | ModerationQueue
  | Commenters
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
    | Series -> "/series"
    | Subscriptions -> "/subscriptions"
    | Settings -> "/settings"
    | NotFound path -> path
