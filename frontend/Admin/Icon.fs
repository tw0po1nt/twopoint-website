[<RequireQualifiedAccess>]
module TwoPoint.Admin.Icon

open Oxpecker.Solid
open Oxpecker.Solid.Aria
open Oxpecker.Solid.Svg

type Icon =
  | Home
  | Posts
  | Comments
  | Commenters
  | Subscriptions
  | Series
  | Settings
  | External
  | SignOut
  | More
  | Menu
  | Close
  | Search
  | Alert
  | Check
  | Inbox
  | Back

/// Each path is on a 24 px grid, for a stroke of 1.75 px.
let pathData (icon: Icon) =
  match icon with
  | Home -> "M3 10.5 12 3l9 7.5V20a1 1 0 0 1-1 1h-5v-6H9v6H4a1 1 0 0 1-1-1z"
  | Posts -> "M14 3H6a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V8zM14 3v5h5M9 13h6M9 17h6"
  | Comments -> "M21 12a8 8 0 0 1-11.6 7.1L4 21l1.9-5.4A8 8 0 1 1 21 12z"
  | Commenters ->
    "M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM22 21v-2a4 4 0 0 0-3-3.9M16 3.1a4 4 0 0 1 0 7.8"
  | Subscriptions -> "M3 5h18v14H3zM3 6l9 7 9-7"
  | Series -> "M12 3 2 8l10 5 10-5zM2 13l10 5 10-5M2 17.5l10 5 10-5"
  | Settings -> "M4 6h9M17 6h3M4 12h3M11 12h9M4 18h11M19 18h1M15 4v4M9 10v4M17 16v4"
  | External -> "M14 4h6v6M20 4l-9 9M18 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5"
  | SignOut -> "M15 3h4a1 1 0 0 1 1 1v16a1 1 0 0 1-1 1h-4M10 17l5-5-5-5M15 12H3"
  | More -> "M5 12h.01M12 12h.01M19 12h.01"
  | Menu -> "M4 6h16M4 12h16M4 18h16"
  | Close -> "M6 6l12 12M18 6 6 18"
  | Search -> "M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14zM21 21l-4.3-4.3"
  | Alert -> "M12 3 2 20h20zM12 10v4M12 17h.01"
  | Check -> "M5 12l5 5L20 7"
  | Inbox -> "M3 13h5l1 3h6l1-3h5M5 5h14l2 8v6H3v-6z"
  | Back -> "M15 6l-6 6 6 6"

let ofRoute (route: Route) =
  match route with
  | Dashboard -> Home
  | Route.Posts
  | Post _
  | PostComments _ -> Posts
  | ModerationQueue -> Comments
  | Route.Commenters -> Commenters
  | Route.Series -> Series
  | Route.Subscriptions -> Subscriptions
  | Route.Settings -> Settings
  | NotFound _ -> Alert

[<SolidComponent>]
let view (icon: Icon) =
  svg (class' = "icon", viewBox = "0 0 24 24", ariaHidden = true) { path (d = pathData icon) }
