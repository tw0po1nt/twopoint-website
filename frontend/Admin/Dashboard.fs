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

type ToDoCount =
  { Title: string
    Count: int option
    Link: Link }

let toDo (queue: int option) (counts: Api.DashboardCounts) =
  [ { Title = "Moderation queue"; Count = queue; Link = ModerationQueue }
    { Title = "Drafts"; Count = Some counts.Drafts; Link = Posts Posts.Drafts }
    { Title = "Pending edits"; Count = Some counts.PendingEdits; Link = Posts Posts.PendingEdits }
    { Title = "Deliveries to start"; Count = Some counts.DeliveriesToStart; Link = Posts Posts.DeliveriesToStart } ]

let audienceText (counts: Api.DashboardCounts) =
  $"+{counts.NewSubscriptions} new, −{counts.CanceledSubscriptions} canceled in the last 30 days"
