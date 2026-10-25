[<RequireQualifiedAccess>]
module TwoPoint.Admin.Posts

type Filter =
  | All
  | Drafts
  | PendingEdits

type Posts =
  | Waiting
  | Loaded of Api.Post list
  | NotLoaded of reason: string

type Model =
  { Filter: Filter
    Posts: Posts
    /// The call that makes a draft is in flight.
    Creating: bool
    CreateFailure: string option
    /// Counts each load of the list. A timer and a poll answer name the load that they belong to.
    Generation: int }

type Msg =
  | Answered of Result<Api.Post list, Api.Failure>
  | Polled of generation: int * Result<Api.Post list, Api.Failure>
  | PollElapsed of generation: int
  | Filtered of Filter
  | NewDraftClicked
  | DraftCreated of Result<Api.CreatedDraft, Api.Failure>

type Effect =
  | CreateDraft
  | Poll of generation: int
  /// Starts a timer of `Deploy.pollDelay` milliseconds. The timer sends `PollElapsed`.
  | WaitToPoll of generation: int

let init =
  { Filter = All
    Posts = Waiting
    Creating = false
    CreateFailure = None
    Generation = 0 }

let reload (model: Model) =
  { model with
      Posts = Waiting
      Generation = model.Generation + 1 }

let filters = [ All, "All"; Drafts, "Drafts"; PendingEdits, "Pending edits" ]

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
  | Filtered filter -> { model with Filter = filter }, None
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

let shown (model: Model) =
  match model.Posts, model.Filter with
  | Loaded posts, All -> posts
  | Loaded posts, Drafts -> posts |> List.filter (fun post -> post.State = Api.Draft)
  | Loaded posts, PendingEdits -> posts |> List.filter (fun post -> post.PendingEdit)
  | (Waiting | NotLoaded _), _ -> []

let emptyText (filter: Filter) =
  match filter with
  | All -> "The blog has no posts."
  | Drafts -> "The blog has no drafts."
  | PendingEdits -> "No post has a pending edit."

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
