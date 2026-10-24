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
    CreateFailure: string option }

type Msg =
  | Answered of Result<Api.Post list, Api.Failure>
  | Filtered of Filter
  | NewDraftClicked
  | DraftCreated of Result<Api.CreatedDraft, Api.Failure>

type Effect = CreateDraft

let init =
  { Filter = All
    Posts = Waiting
    Creating = false
    CreateFailure = None }

let reload (model: Model) = { model with Posts = Waiting }

let filters = [ All, "All"; Drafts, "Drafts"; PendingEdits, "Pending edits" ]

let update (msg: Msg) (model: Model) : Model * Effect option =
  match msg with
  | Answered(Ok posts) -> { model with Posts = Loaded posts }, None
  | Answered(Error failure) -> { model with Posts = NotLoaded(Api.reason failure) }, None
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

let stateText (state: Api.PostState) =
  match state with
  | Api.Draft -> "Draft"
  | Api.Published -> "Published"
  | Api.Upcoming -> "Upcoming"

/// The API sends each date in ISO 8601.
let dateText (date: string) = date.Split 'T' |> Array.head
