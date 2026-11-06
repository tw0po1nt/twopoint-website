[<RequireQualifiedAccess>]
module TwoPoint.Admin.PostComments

type Filter =
  | All
  | Only of Api.CommentApproval

type Row =
  { Comment: Api.Comment
    State: Queue.RowState }

type Comments =
  | Waiting
  | Loaded of Row list
  | NotLoaded of reason: string

type Model =
  { Slug: string
    Filter: Filter
    Comments: Comments }

type Msg =
  /// An answer for another post comes after the author left its page, and changes nothing.
  | Answered of slug: string * Result<Api.Comment list, Api.Failure>
  | Filtered of Filter
  /// A delete comes here only after the confirmation.
  | Chosen of commentId: string * Queue.Action
  | Done of commentId: string * Queue.Action * Result<unit, Api.Failure>

type Effect = Moderate of slug: string * Api.Comment * Queue.Action

let init (slug: string) =
  { Slug = slug
    Filter = All
    Comments = Waiting }

let isLoaded (model: Model) =
  match model.Comments with
  | Loaded _ -> true
  | Waiting
  | NotLoaded _ -> false

let approvalText (approval: Api.CommentApproval) =
  match approval with
  | Api.CommentApproval.New -> "New"
  | Api.CommentApproval.Approved -> "Approved"
  | Api.CommentApproval.Rejected -> "Rejected"

let filters =
  (All, "All")
  :: ([ Api.CommentApproval.New; Api.CommentApproval.Approved; Api.CommentApproval.Rejected ]
      |> List.map (fun approval -> Only approval, approvalText approval))

/// The backend refuses a change to the approval that the comment has.
let actions (approval: Api.CommentApproval) =
  match approval with
  | Api.CommentApproval.New -> [ Queue.Approve; Queue.Reject; Queue.Delete ]
  | Api.CommentApproval.Approved -> [ Queue.Reject; Queue.Delete ]
  | Api.CommentApproval.Rejected -> [ Queue.Approve; Queue.Delete ]

let private id (row: Row) = row.Comment.Id

let private change (commentId: string) (next: Row -> Row) (rows: Row list) =
  rows |> List.map (fun row -> if id row = commentId then next row else row)

let private withApproval (approval: Api.CommentApproval) (row: Row) =
  { Comment = { row.Comment with Status = approval }
    State = Progress.Ready }

let private moderated (commentId: string) (action: Queue.Action) (rows: Row list) =
  match action with
  | Queue.Approve
  | Queue.ApproveAndTrust -> change commentId (withApproval Api.CommentApproval.Approved) rows
  | Queue.Reject -> change commentId (withApproval Api.CommentApproval.Rejected) rows
  | Queue.Delete -> rows |> List.filter (fun row -> id row <> commentId)

let private choose (commentId: string) (action: Queue.Action) (rows: Row list) (model: Model) =
  match rows |> List.tryFind (fun row -> id row = commentId) with
  | Some({ State = Progress.Ready | Progress.NotDone _ } as row) when List.contains action (actions row.Comment.Status) ->
    { model with Comments = Loaded(change commentId (fun row -> { row with State = Progress.Sending action }) rows) },
    Some(Moderate(model.Slug, row.Comment, action))
  | Some _
  | None -> model, None

let update (msg: Msg) (model: Model) : Model * Effect option =
  match model.Comments, msg with
  | _, Answered(slug, _) when slug <> model.Slug -> model, None
  | _, Answered(_, Ok comments) ->
    { model with Comments = Loaded(comments |> List.map (fun comment -> { Comment = comment; State = Progress.Ready })) },
    None
  | _, Answered(_, Error failure) -> { model with Comments = NotLoaded(Api.reason failure) }, None
  | _, Filtered filter -> { model with Filter = filter }, None
  | Loaded rows, Chosen(commentId, action) -> choose commentId action rows model
  | Loaded rows, Done(commentId, action, Ok()) -> { model with Comments = Loaded(moderated commentId action rows) }, None
  | Loaded rows, Done(commentId, action, Error failure) ->
    { model with Comments = Loaded(change commentId (fun row -> { row with State = Progress.NotDone(action, Api.reason failure) }) rows) },
    None
  | (Waiting | NotLoaded _), (Chosen _ | Done _) -> model, None

let shown (model: Model) =
  match model.Comments, model.Filter with
  | Loaded rows, All -> rows
  | Loaded rows, Only approval -> rows |> List.filter (fun row -> row.Comment.Status = approval)
  | (Waiting | NotLoaded _), _ -> []

let tryFind (commentId: string) (model: Model) =
  match model.Comments with
  | Loaded rows -> rows |> List.tryFind (fun row -> id row = commentId)
  | Waiting
  | NotLoaded _ -> None

let emptyText (filter: Filter) =
  match filter with
  | All -> "No comments yet."
  | Only Api.CommentApproval.New -> "No new comments."
  | Only Api.CommentApproval.Approved -> "No approved comments."
  | Only Api.CommentApproval.Rejected -> "No rejected comments."
