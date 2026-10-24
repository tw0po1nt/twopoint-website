[<RequireQualifiedAccess>]
module TwoPoint.Admin.Queue

type Action =
  | Approve
  | Reject
  | Delete

type RowState =
  | Ready
  | Sending of Action
  | NotDone of Action * reason: string

type Row =
  { Comment: Api.QueuedComment
    State: RowState }

type Model =
  | Waiting
  | Loaded of Row list
  | NotLoaded of reason: string

type Msg =
  | Answered of Result<Api.QueuedComment list, Api.Failure>
  /// A delete comes here only after the confirmation.
  | Chosen of commentId: string * Action
  | Done of commentId: string * Action * Result<unit, Api.Failure>

type Effect = Moderate of Api.QueuedComment * Action

let init: Model = Waiting

let private id (row: Row) = row.Comment.Comment.Id

let private change (commentId: string) (state: RowState) (rows: Row list) =
  rows |> List.map (fun row -> if id row = commentId then { row with State = state } else row)

let private choose (commentId: string) (action: Action) (rows: Row list) =
  match rows |> List.tryFind (fun row -> id row = commentId) with
  | Some({ State = Ready | NotDone _ } as row) ->
    Loaded(change commentId (Sending action) rows), Some(Moderate(row.Comment, action))
  | Some { State = Sending _ }
  | None -> Loaded rows, None

let update (msg: Msg) (model: Model) : Model * Effect option =
  match model, msg with
  | _, Answered(Ok comments) -> Loaded(comments |> List.map (fun comment -> { Comment = comment; State = Ready })), None
  | _, Answered(Error failure) -> NotLoaded(Api.reason failure), None
  | Loaded rows, Chosen(commentId, action) -> choose commentId action rows
  | Loaded rows, Done(commentId, _, Ok()) -> Loaded(rows |> List.filter (fun row -> id row <> commentId)), None
  | Loaded rows, Done(commentId, action, Error failure) ->
    Loaded(change commentId (NotDone(action, Api.reason failure)) rows), None
  | (Waiting | NotLoaded _), (Chosen _ | Done _) -> model, None

let count (model: Model) =
  match model with
  | Loaded rows -> Some(List.length rows)
  | Waiting
  | NotLoaded _ -> None

let tryFind (commentId: string) (model: Model) =
  match model with
  | Loaded rows -> rows |> List.tryFind (fun row -> id row = commentId)
  | Waiting
  | NotLoaded _ -> None

let isSending (row: Row) =
  match row.State with
  | Sending _ -> true
  | Ready
  | NotDone _ -> false

let reason (row: Row) =
  match row.State with
  | NotDone(Approve, reason) -> Some $"The approval did not complete. {reason}"
  | NotDone(Reject, reason) -> Some $"The rejection did not complete. {reason}"
  | NotDone(Delete, reason) -> Some $"The delete did not complete. {reason}"
  | Ready
  | Sending _ -> None

let statusText (status: Api.CommenterStatus) =
  match status with
  | Api.New -> "New"
  | Api.Trusted -> "Trusted"
  | Api.Banned -> "Banned"
