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
    /// The call of "retry" is in flight.
    Retrying: bool
    RetryFailure: string option }

type Msg =
  | Answered of Result<Api.Post list, Api.Failure>
  /// The answer of a read that keeps the list on the screen.
  | Polled of Result<Api.Post list, Api.Failure>
  | PollElapsed
  | Filtered of Filter
  | NewDraftClicked
  | DraftCreated of Result<Api.CreatedDraft, Api.Failure>
  | RetryClicked
  | Retried of Result<Api.Rebuilt, Api.Failure>

type Effect =
  | CreateDraft
  | Poll
  /// Starts a timer of `Deploy.pollDelay` milliseconds. The timer sends `PollElapsed`.
  | WaitToPoll
  | Rebuild

let init =
  { Filter = All
    Posts = Waiting
    Creating = false
    CreateFailure = None
    Retrying = false
    RetryFailure = None }

let reload (model: Model) = { model with Posts = Waiting }

let filters = [ All, "All"; Drafts, "Drafts"; PendingEdits, "Pending edits" ]

let private isDeploying (posts: Api.Post list) =
  posts |> List.exists (fun post -> post.Deploy = Api.DeployState.Deploying)

let private waitIfDeploying (posts: Api.Post list) =
  if isDeploying posts then Some WaitToPoll else None

let update (msg: Msg) (model: Model) : Model * Effect option =
  match msg with
  | Answered(Ok posts)
  | Polled(Ok posts) -> { model with Posts = Loaded posts }, waitIfDeploying posts
  | Answered(Error failure) -> { model with Posts = NotLoaded(Api.reason failure) }, None
  | Polled(Error Api.Unanswered) -> model, Some WaitToPoll
  | Polled(Error _) -> model, None
  | PollElapsed ->
    match model.Posts with
    | Loaded posts when isDeploying posts -> model, Some Poll
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
  | RetryClicked when model.Retrying -> model, None
  | RetryClicked ->
    { model with
        Retrying = true
        RetryFailure = None },
    Some Rebuild
  | Retried(Ok _) ->
    { model with
        Retrying = false
        RetryFailure = None },
    Some Poll
  | Retried(Error failure) ->
    { model with
        Retrying = false
        RetryFailure = Some(Api.reason failure) },
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

/// A draft is not on the site, so it has no deploy state.
let showsDeploy (post: Api.Post) =
  match post.State with
  | Api.Draft -> false
  | Api.Published
  | Api.Upcoming -> true

let canRetry (post: Api.Post) =
  showsDeploy post && post.Deploy = Api.DeployState.DeployFailed

let stateText (state: Api.PostState) =
  match state with
  | Api.Draft -> "Draft"
  | Api.Published -> "Published"
  | Api.Upcoming -> "Upcoming"
