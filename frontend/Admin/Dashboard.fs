[<RequireQualifiedAccess>]
module TwoPoint.Admin.Dashboard

type Model =
  | Waiting
  | Loaded of Api.DashboardCounts
  | NotLoaded of reason: string

type Msg = Answered of Result<Api.DashboardCounts, Api.Failure>

let init: Model = Waiting

let update (msg: Msg) (model: Model) : Model =
  match msg with
  | Answered(Ok counts) -> Loaded counts
  | Answered(Error failure) -> NotLoaded(Api.reason failure)

type ToDoCount =
  { Title: string
    Count: int option
    Route: Route }

let toDo (queue: int option) (counts: Api.DashboardCounts) =
  [ { Title = "Moderation queue"
      Count = queue
      Route = Route.ModerationQueue }
    { Title = "Drafts"
      Count = Some counts.Drafts
      Route = Route.Posts PostFilter.Drafts }
    { Title = "Pending edits"
      Count = Some counts.PendingEdits
      Route = Route.Posts PostFilter.PendingEdits }
    { Title = "Pending deliveries"
      Count = Some counts.DeliveriesToStart
      Route = Route.Posts PostFilter.PendingDeliveries } ]

let private signed (sign: string) (count: int) =
  if count > 0 then $"{sign}{count}" else string count

let audienceText (counts: Api.DashboardCounts) =
  let made = signed "+" counts.NewSubscriptions
  let canceled = signed "−" counts.CanceledSubscriptions
  $"{made} new, {canceled} canceled in the last 30 days"
