[<RequireQualifiedAccess>]
module TwoPoint.Admin.Posts

type Posts =
  | Waiting
  | Loaded of Api.Post list
  | NotLoaded of reason: string

type Model =
  { Posts: Posts
    /// The call that makes a draft is in flight.
    Creating: bool
    CreateFailure: string option
    /// The warnings of the last delete of a post. The author dismisses them.
    Notice: Api.ChangeWarning list
    /// Counts each load of the list. A timer and a poll answer name the load that they belong to.
    Generation: int }

type Msg =
  | Answered of Result<Api.Post list, Api.Failure>
  | Polled of generation: int * Result<Api.Post list, Api.Failure>
  | PollElapsed of generation: int
  | NewDraftClicked
  | DraftCreated of Result<Api.CreatedDraft, Api.Failure>
  | NoticeDismissed

type Effect =
  | CreateDraft
  | Poll of generation: int
  /// Starts a timer of `Deploy.pollDelay` milliseconds. The timer sends `PollElapsed`.
  | WaitToPoll of generation: int

let init =
  { Posts = Waiting
    Creating = false
    CreateFailure = None
    Notice = []
    Generation = 0 }

/// The rows stay until the answer, because a back from the editor aims its light at the row of the post.
let reload (model: Model) =
  { model with
      Posts =
        match model.Posts with
        | Loaded posts -> Loaded posts
        | Waiting
        | NotLoaded _ -> Waiting
      Generation = model.Generation + 1 }

let filters =
  [ PostFilter.All, "All"
    PostFilter.Drafts, "Drafts"
    PostFilter.PendingEdits, "Pending edits"
    PostFilter.DeliveriesToStart, "Deliveries to start" ]

let private isDeploying (posts: Api.Post list) =
  posts |> List.exists (fun post -> post.Deploy = Api.DeployState.Deploying)

let private waitIfDeploying (generation: int) (posts: Api.Post list) =
  if isDeploying posts then Some(WaitToPoll generation) else None

let update (msg: Msg) (model: Model) : Model * Effect option =
  match msg with
  | Answered(Ok posts) -> { model with Posts = Loaded posts }, waitIfDeploying model.Generation posts
  | Answered(Error failure) -> { model with Posts = NotLoaded(Api.reason failure) }, None
  | Polled(generation, _) when generation <> model.Generation -> model, None
  | Polled(generation, Ok posts) -> { model with Posts = Loaded posts }, waitIfDeploying generation posts
  | Polled(generation, Error Api.Unanswered) -> model, Some(WaitToPoll generation)
  | Polled(_, Error _) -> model, None
  | PollElapsed generation when generation <> model.Generation -> model, None
  | PollElapsed generation ->
    match model.Posts with
    | Loaded posts when isDeploying posts -> model, Some(Poll generation)
    | Waiting
    | Loaded _
    | NotLoaded _ -> model, None
  | NoticeDismissed -> { model with Notice = [] }, None
  | NewDraftClicked when model.Creating -> model, None
  | NewDraftClicked ->
    { model with
        Creating = true
        CreateFailure = None },
    Some CreateDraft
  | DraftCreated(Ok _) -> { model with Creating = false }, None
  | DraftCreated(Error failure) ->
    { model with
        Creating = false
        CreateFailure = Some(Api.reason failure) },
    None

let shown (filter: PostFilter) (model: Model) =
  match model.Posts, filter with
  | Loaded posts, PostFilter.All -> posts
  | Loaded posts, PostFilter.Drafts -> posts |> List.filter (fun post -> post.State = Api.Draft)
  | Loaded posts, PostFilter.PendingEdits -> posts |> List.filter (fun post -> post.PendingEdit)
  | Loaded posts, PostFilter.DeliveriesToStart -> posts |> List.filter (fun post -> post.DeliveryToStart)
  | (Waiting | NotLoaded _), _ -> []

let emptyText (filter: PostFilter) =
  match filter with
  | PostFilter.All -> "The blog has no posts."
  | PostFilter.Drafts -> "The blog has no drafts."
  | PostFilter.PendingEdits -> "No post has a pending edit."
  | PostFilter.DeliveriesToStart -> "No post waits for a delivery."

/// The name of a post on the page of its comments. A list with no post of the slug gives the slug.
type PostName =
  | NameWaiting
  | Name of string

let postName (slug: string) (model: Model) =
  match model.Posts with
  | Waiting -> NameWaiting
  | Loaded posts ->
    posts
    |> List.tryFind (fun post -> post.Slug = slug)
    |> Option.map _.Title
    |> Option.defaultValue slug
    |> Name
  | NotLoaded _ -> Name slug

let commentsText (stats: Api.CommentStats) =
  $"{stats.New} new, {stats.Approved} approved, {stats.Rejected} rejected comments"

let showsDeploy (post: Api.Post) =
  match post.State with
  | Api.Draft -> false
  | Api.Published
  | Api.Upcoming -> true

let stateText (state: Api.PostState) =
  match state with
  | Api.Draft -> "Draft"
  | Api.Published -> "Published"
  | Api.Upcoming -> "Upcoming"
