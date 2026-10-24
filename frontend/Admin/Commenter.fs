[<RequireQualifiedAccess>]
module TwoPoint.Admin.Commenter

type Action =
  | Trust
  | Untrust
  | Ban
  | Unban
  | Delete

type State =
  | Ready
  | Sending of Action
  | NotDone of Action * reason: string

type Page =
  | Waiting
  | Loaded of Api.CommenterComments * State
  | NotLoaded of reason: string

type Model = { Id: string; Page: Page }

type Msg =
  /// An answer for another commenter comes after the author left its page, and changes nothing.
  | Answered of commenterId: string * Result<Api.CommenterComments, Api.Failure>
  /// A delete comes here only after the confirmation.
  | Chosen of Action
  | Done of commenterId: string * Action * Result<unit, Api.Failure>

type Effect = Change of commenterId: string * Action

let init (commenterId: string) = { Id = commenterId; Page = Waiting }

/// The backend refuses to trust a banned commenter.
let actions (status: Api.CommenterStatus) =
  match status with
  | Api.New -> [ Trust; Ban; Delete ]
  | Api.Trusted -> [ Untrust; Ban; Delete ]
  | Api.Banned -> [ Unban; Delete ]

let label (action: Action) =
  match action with
  | Trust -> "Trust"
  | Untrust -> "Untrust"
  | Ban -> "Ban"
  | Unban -> "Unban"
  | Delete -> "Delete"

/// None for a delete, because a delete removes the commenter.
let statusAfter (action: Action) =
  match action with
  | Trust -> Some Api.Trusted
  | Untrust
  | Unban -> Some Api.New
  | Ban -> Some Api.Banned
  | Delete -> None

/// The ban rejects each new comment in the backend, so the page does the same.
let private changed (status: Api.CommenterStatus) (page: Api.CommenterComments) =
  let reject (comment: Api.QueuedComment) =
    match status, comment.Comment.Status with
    | Api.Banned, Api.CommentApproval.New ->
      { comment with
          Comment.Status = Api.CommentApproval.Rejected }
    | _ -> comment

  { page with
      Commenter = { page.Commenter with Status = status }
      Comments = page.Comments |> List.map reject }

let private choose (action: Action) (model: Model) =
  match model.Page with
  | Loaded(page, (Ready | NotDone _)) when List.contains action (actions page.Commenter.Status) ->
    { model with Page = Loaded(page, Sending action) }, Some(Change(model.Id, action))
  | Loaded _
  | Waiting
  | NotLoaded _ -> model, None

let update (msg: Msg) (model: Model) : Model * Effect option =
  match model.Page, msg with
  | _, Answered(commenterId, _)
  | _, Done(commenterId, _, _) when commenterId <> model.Id -> model, None
  | _, Answered(_, Ok page) -> { model with Page = Loaded(page, Ready) }, None
  | _, Answered(_, Error failure) -> { model with Page = NotLoaded(Api.reason failure) }, None
  | _, Chosen action -> choose action model
  | Loaded(page, _), Done(_, action, Ok()) ->
    match statusAfter action with
    | Some status -> { model with Page = Loaded(changed status page, Ready) }, None
    | None -> model, None
  | Loaded(page, _), Done(_, action, Error failure) ->
    { model with Page = Loaded(page, NotDone(action, Api.reason failure)) }, None
  | (Waiting | NotLoaded _), Done _ -> model, None

let tryCommenter (model: Model) =
  match model.Page with
  | Loaded(page, _) -> Some page.Commenter
  | Waiting
  | NotLoaded _ -> None

let sends (state: State) =
  match state with
  | Sending _ -> true
  | Ready
  | NotDone _ -> false

let failure (state: State) =
  match state with
  | NotDone(Trust, reason) -> Some $"The trust did not complete. {reason}"
  | NotDone(Untrust, reason) -> Some $"The untrust did not complete. {reason}"
  | NotDone(Ban, reason) -> Some $"The ban did not complete. {reason}"
  | NotDone(Unban, reason) -> Some $"The unban did not complete. {reason}"
  | NotDone(Delete, reason) -> Some $"The delete did not complete. {reason}"
  | Ready
  | Sending _ -> None
