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
    Comments: Comments
    Reply: AuthorReply.Form option }

type Msg =
  /// An answer for another post comes after the author left its page, and changes nothing.
  | Answered of slug: string * Result<Api.Comment list, Api.Failure>
  | Filtered of Filter
  /// A delete comes here only after the confirmation.
  | Chosen of commentId: string * Queue.Action
  | Done of commentId: string * Queue.Action * Result<unit, Api.Failure>
  | Reply of AuthorReply.Msg

type Effect =
  | Moderate of slug: string * Api.Comment * Queue.Action
  | PreviewReply of commentId: string * TwoPoint.Site.Widgets.Comments.PreviewRequest
  | PostReply of slug: string * commentId: string * content: string

let init (slug: string) =
  { Slug = slug
    Filter = All
    Comments = Waiting
    Reply = None }

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

let private ready (comment: Api.Comment) = { Comment = comment; State = Progress.Ready }

/// The approval of the top-level comment of a reply. A top-level comment has none.
let private topLevelIn (rows: Row list) (comment: Api.Comment) =
  comment.ReplyTo
  |> Option.bind (fun topLevel -> rows |> List.tryFind (fun row -> id row = topLevel))
  |> Option.map (fun row -> row.Comment.Status)

/// Closes the reply form when its comment left the list or can no longer take a reply.
let private keepReply (rows: Row list) (form: AuthorReply.Form option) =
  form
  |> AuthorReply.keepIf (fun commentId ->
    rows
    |> List.exists (fun row -> id row = commentId && AuthorReply.canReply (topLevelIn rows row.Comment) row.Comment.Status))

/// The reply comes last, because the list is oldest first.
let private replied (answer: Api.AuthorReply) (rows: Row list) =
  let approve row =
    if List.contains (id row) answer.Approved then withApproval Api.CommentApproval.Approved row else row

  (rows |> List.map approve) @ [ ready answer.Reply ]

let private reply (msg: AuthorReply.Msg) (model: Model) : Model * Effect option =
  match model.Comments, msg with
  | Loaded rows, AuthorReply.Opened commentId ->
    match rows |> List.tryFind (fun row -> id row = commentId) with
    | Some row when not (AuthorReply.isOpenOn commentId model.Reply) ->
      { model with Reply = AuthorReply.opened (topLevelIn rows row.Comment) row.Comment }, None
    | Some _
    | None -> model, None
  | Loaded rows, AuthorReply.Answered(commentId, Ok answer) when AuthorReply.isOpenOn commentId model.Reply ->
    { model with
        Comments = Loaded(replied answer rows)
        Reply = None },
    None
  | _ ->
    let form, effect = AuthorReply.update msg model.Reply

    let effect =
      effect
      |> Option.map (fun effect ->
        match effect with
        | AuthorReply.Preview(commentId, request) -> PreviewReply(commentId, request)
        | AuthorReply.Post(commentId, content) -> PostReply(model.Slug, commentId, content))

    { model with Reply = form }, effect

let update (msg: Msg) (model: Model) : Model * Effect option =
  match model.Comments, msg with
  | _, Answered(slug, _) when slug <> model.Slug -> model, None
  | _, Answered(_, Ok comments) ->
    let rows = comments |> List.map ready

    { model with
        Comments = Loaded rows
        Reply = keepReply rows model.Reply },
    None
  | _, Answered(_, Error failure) -> { model with Comments = NotLoaded(Api.reason failure) }, None
  | _, Filtered filter -> { model with Filter = filter }, None
  | Loaded rows, Chosen(commentId, action) -> choose commentId action rows model
  | Loaded rows, Done(commentId, action, Ok()) ->
    let rows = moderated commentId action rows

    { model with
        Comments = Loaded rows
        Reply = keepReply rows model.Reply },
    None
  | Loaded rows, Done(commentId, action, Error failure) ->
    { model with Comments = Loaded(change commentId (fun row -> { row with State = Progress.NotDone(action, Api.reason failure) }) rows) },
    None
  | _, Reply msg -> reply msg model
  | (Waiting | NotLoaded _), (Chosen _ | Done _) -> model, None

let shown (model: Model) =
  match model.Comments, model.Filter with
  | Loaded rows, All -> rows
  | Loaded rows, Only approval -> rows |> List.filter (fun row -> row.Comment.Status = approval)
  | (Waiting | NotLoaded _), _ -> []

let private rowsOf (model: Model) =
  match model.Comments with
  | Loaded rows -> rows
  | Waiting
  | NotLoaded _ -> []

/// Each top-level comment that the filter shows, and each reply whose top-level comment the filter hides.
/// A thread keeps the place of its top-level comment in the list, which is oldest first.
let heads (model: Model) =
  let rows = rowsOf model
  let shown = shown model
  let shownIds = shown |> List.map id |> Set.ofList
  let place (commentId: string) = rows |> List.tryFindIndex (fun row -> id row = commentId)

  shown
  |> List.filter (fun row -> row.Comment.ReplyTo |> Option.forall (shownIds.Contains >> not))
  |> List.sortBy (fun row ->
    let own = place (id row) |> Option.defaultValue 0
    row.Comment.ReplyTo |> Option.bind place |> Option.defaultValue own, own)

/// The replies of a top-level comment that the filter shows, oldest first. A hidden top-level comment has none.
let replies (topLevelId: string) (model: Model) =
  let shown = shown model

  if shown |> List.exists (fun row -> id row = topLevelId) then
    shown |> List.filter (fun row -> row.Comment.ReplyTo = Some topLevelId)
  else
    []

/// The top-level comment of a reply, with the approval that it has on the page.
let topLevel (comment: Api.Comment) (model: Model) =
  comment.TopLevel
  |> Option.map (fun topLevel ->
    match rowsOf model |> List.tryFind (fun row -> id row = topLevel.Id) with
    | Some row -> { topLevel with Status = row.Comment.Status }
    | None -> topLevel)

let tryFind (commentId: string) (model: Model) =
  match model.Comments with
  | Loaded rows -> rows |> List.tryFind (fun row -> id row = commentId)
  | Waiting
  | NotLoaded _ -> None

let topLevelApproval (comment: Api.Comment) (model: Model) =
  match model.Comments with
  | Loaded rows -> topLevelIn rows comment
  | Waiting
  | NotLoaded _ -> None

let emptyText (filter: Filter) =
  match filter with
  | All -> "No comments yet."
  | Only Api.CommentApproval.New -> "No new comments."
  | Only Api.CommentApproval.Approved -> "No approved comments."
  | Only Api.CommentApproval.Rejected -> "No rejected comments."
