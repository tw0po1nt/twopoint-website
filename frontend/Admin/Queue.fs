[<RequireQualifiedAccess>]
module TwoPoint.Admin.Queue

type Action =
  | Approve
  | ApproveAndTrust
  | Reject
  | Delete

type RowState = Progress<Action>

type Row =
  { Comment: Api.PostedComment
    State: RowState }

type Model =
  | Waiting
  | Loaded of Row list
  | NotLoaded of reason: string

type Msg =
  | Answered of Result<Api.PostedComment list, Api.Failure>
  /// A delete comes here only after the confirmation.
  | Chosen of commentId: string * Action
  | Trusted of commentId: string * Result<unit, Api.Failure>
  | Done of commentId: string * Action * Result<unit, Api.Failure>

type Effect =
  | Trust of Api.PostedComment
  | Moderate of Api.PostedComment * Action

let init: Model = Waiting

/// The backend refuses to trust a banned commenter.
let actions (status: Api.CommenterStatus) =
  match status with
  | Api.New -> [ Approve; ApproveAndTrust; Reject; Delete ]
  | Api.Trusted
  | Api.Banned -> [ Approve; Reject; Delete ]

let private id (row: Row) = row.Comment.Comment.Id

let private commenter (row: Row) = row.Comment.Comment.Commenter

let private change (commentId: string) (state: RowState) (rows: Row list) =
  rows |> List.map (fun row -> if id row = commentId then { row with State = state } else row)

let private find (commentId: string) (rows: Row list) =
  rows |> List.tryFind (fun row -> id row = commentId)

let private withTrust (commenterId: string) (row: Row) =
  if (commenter row).Id = commenterId then
    { row with
        Comment.Comment.Commenter.Status = Api.Trusted }
  else
    row

let private choose (commentId: string) (action: Action) (rows: Row list) =
  match find commentId rows with
  | Some({ State = Progress.Ready | Progress.NotDone _ } as row) when List.contains action (actions (commenter row).Status) ->
    let effect =
      match action with
      | ApproveAndTrust -> Trust row.Comment
      | Approve
      | Reject
      | Delete -> Moderate(row.Comment, action)

    Loaded(change commentId (Progress.Sending action) rows), Some effect
  | Some _
  | None -> Loaded rows, None

let private trusted (commentId: string) (rows: Row list) =
  match find commentId rows with
  | Some row ->
    let trust = withTrust (commenter row).Id
    Loaded(rows |> List.map trust), Some(Moderate((trust row).Comment, Approve))
  | None -> Loaded rows, None

let update (msg: Msg) (model: Model) : Model * Effect option =
  match model, msg with
  | _, Answered(Ok comments) -> Loaded(comments |> List.map (fun comment -> { Comment = comment; State = Progress.Ready })), None
  | _, Answered(Error failure) -> NotLoaded(Api.reason failure), None
  | Loaded rows, Chosen(commentId, action) -> choose commentId action rows
  | Loaded rows, Trusted(commentId, Ok()) -> trusted commentId rows
  | Loaded rows, Trusted(commentId, Error failure) ->
    Loaded(change commentId (Progress.NotDone(ApproveAndTrust, Api.reason failure)) rows), None
  | Loaded rows, Done(commentId, _, Ok()) -> Loaded(rows |> List.filter (fun row -> id row <> commentId)), None
  | Loaded rows, Done(commentId, action, Error failure) ->
    Loaded(change commentId (Progress.NotDone(action, Api.reason failure)) rows), None
  | (Waiting | NotLoaded _), (Chosen _ | Trusted _ | Done _) -> model, None

let count (model: Model) =
  match model with
  | Loaded rows -> Some(List.length rows)
  | Waiting
  | NotLoaded _ -> None

let tryFind (commentId: string) (model: Model) =
  match model with
  | Loaded rows -> find commentId rows
  | Waiting
  | NotLoaded _ -> None

let failure (state: RowState) =
  match state with
  | Progress.NotDone(Approve, reason) -> Some $"Couldn't approve the comment. {reason}"
  | Progress.NotDone(ApproveAndTrust, reason) -> Some $"Couldn't trust the commenter, so the comment wasn't approved. {reason}"
  | Progress.NotDone(Reject, reason) -> Some $"Couldn't reject the comment. {reason}"
  | Progress.NotDone(Delete, reason) -> Some $"Couldn't delete the comment. {reason}"
  | Progress.Ready
  | Progress.Sending _ -> None

let statusText (status: Api.CommenterStatus) =
  match status with
  | Api.New -> "New"
  | Api.Trusted -> "Trusted"
  | Api.Banned -> "Banned"
