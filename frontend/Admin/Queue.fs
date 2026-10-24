[<RequireQualifiedAccess>]
module TwoPoint.Admin.Queue

type Action =
  | Approve
  | ApproveAndTrust
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

let private trusted (commenterId: string) (row: Row) =
  if (commenter row).Id = commenterId then
    { row with
        Comment.Comment.Commenter.Status = Api.Trusted }
  else
    row

let private moderated (commentId: string) (action: Action) (rows: Row list) =
  let others = rows |> List.filter (fun row -> id row <> commentId)

  match action, rows |> List.tryFind (fun row -> id row = commentId) with
  | ApproveAndTrust, Some row -> others |> List.map (trusted (commenter row).Id)
  | ApproveAndTrust, None
  | (Approve | Reject | Delete), _ -> others

let private choose (commentId: string) (action: Action) (rows: Row list) =
  match rows |> List.tryFind (fun row -> id row = commentId) with
  | Some({ State = Ready | NotDone _ } as row) when List.contains action (actions (commenter row).Status) ->
    Loaded(change commentId (Sending action) rows), Some(Moderate(row.Comment, action))
  | Some _
  | None -> Loaded rows, None

let update (msg: Msg) (model: Model) : Model * Effect option =
  match model, msg with
  | _, Answered(Ok comments) -> Loaded(comments |> List.map (fun comment -> { Comment = comment; State = Ready })), None
  | _, Answered(Error failure) -> NotLoaded(Api.reason failure), None
  | Loaded rows, Chosen(commentId, action) -> choose commentId action rows
  | Loaded rows, Done(commentId, action, Ok()) -> Loaded(moderated commentId action rows), None
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

let sends (state: RowState) =
  match state with
  | Sending _ -> true
  | Ready
  | NotDone _ -> false

let failure (state: RowState) =
  match state with
  | NotDone(Approve, reason) -> Some $"The approval did not complete. {reason}"
  | NotDone(ApproveAndTrust, reason) -> Some $"The approval and the trust did not complete. {reason}"
  | NotDone(Reject, reason) -> Some $"The rejection did not complete. {reason}"
  | NotDone(Delete, reason) -> Some $"The delete did not complete. {reason}"
  | Ready
  | Sending _ -> None

let statusText (status: Api.CommenterStatus) =
  match status with
  | Api.New -> "New"
  | Api.Trusted -> "Trusted"
  | Api.Banned -> "Banned"
