[<RequireQualifiedAccess>]
module TwoPoint.Admin.Dashboard

type Model =
  | Waiting
  | Loaded of Api.DashboardCounts
  | NotLoaded of reason: string

type Link =
  | ModerationQueue
  | Posts of Posts.Filter
  | Subscriptions of Subscriptions.Filter

type Msg =
  | Answered of Result<Api.DashboardCounts, Api.Failure>
  /// The Shell sets the filter of the page and goes to the page.
  | LinkFollowed of Link

let init: Model = Waiting

let update (msg: Msg) (model: Model) : Model =
  match msg with
  | Answered(Ok counts) -> Loaded counts
  | Answered(Error failure) -> NotLoaded(Api.reason failure)
  | LinkFollowed _ -> model

let route (link: Link) =
  match link with
  | ModerationQueue -> Route.ModerationQueue
  | Posts _ -> Route.Posts
  | Subscriptions _ -> Route.Subscriptions

let toDo (queue: int option) (counts: Api.DashboardCounts) =
  [ "Moderation queue", queue, ModerationQueue
    "Drafts", Some counts.Drafts, Posts Posts.Drafts
    "Pending edits", Some counts.PendingEdits, Posts Posts.PendingEdits
    "Deliveries to start", Some counts.DeliveriesToStart, Posts Posts.DeliveriesToStart ]

let audienceText (counts: Api.DashboardCounts) =
  $"+{counts.NewSubscriptions} new, −{counts.CanceledSubscriptions} canceled in the last 30 days"
