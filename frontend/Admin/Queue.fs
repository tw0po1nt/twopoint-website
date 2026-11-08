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

type Comments =
  | Waiting
  | Loaded of Row list
  | NotLoaded of reason: string

type Model =
  { Comments: Comments
    Reply: AuthorReply.Form option
    Opened: ReplyContext.Opened }

type Msg =
  | Answered of Result<Api.PostedComment list, Api.Failure>
  /// A delete comes here only after the confirmation.
  | Chosen of commentId: string * Action
  | Trusted of commentId: string * Result<unit, Api.Failure>
  | Done of commentId: string * Action * Result<unit, Api.Failure>
  | Reply of AuthorReply.Msg
  | TopLevelToggled of commentId: string

type Effect =
  | Trust of Api.PostedComment * Api.Commenter
  | Moderate of Api.PostedComment * Action
  | PreviewReply of commentId: string * TwoPoint.Site.Widgets.Comments.PreviewRequest
  | PostReply of Api.PostedComment * content: string

let init: Model =
  { Comments = Waiting
    Reply = None
    Opened = Set.empty }

/// The backend refuses to trust a banned commenter.
let actions (status: Api.CommenterStatus) =
  match status with
  | Api.New -> [ Approve; ApproveAndTrust; Reject; Delete ]
  | Api.Trusted
  | Api.Banned -> [ Approve; Reject; Delete ]

/// The author has no commenter record to trust.
let writerActions (writer: Api.Writer) =
  match writer with
  | Api.Writer.Commenter commenter -> actions commenter.Status
  | Api.Writer.Author -> [ Approve; Reject; Delete ]

let private id (row: Row) = row.Comment.Comment.Id

let private commenterId (row: Row) =
  match row.Comment.Comment.Writer with
  | Api.Writer.Commenter commenter -> Some commenter.Id
  | Api.Writer.Author -> None

let private change (commentId: string) (state: RowState) (rows: Row list) =
  rows |> List.map (fun row -> if id row = commentId then { row with State = state } else row)

let private find (commentId: string) (rows: Row list) =
  rows |> List.tryFind (fun row -> id row = commentId)

let private withTrust (trustedId: string) (row: Row) =
  match row.Comment.Comment.Writer with
  | Api.Writer.Commenter commenter when commenter.Id = trustedId ->
    { row with
        Comment.Comment.Writer = Api.Writer.Commenter { commenter with Status = Api.Trusted } }
  | Api.Writer.Commenter _
  | Api.Writer.Author -> row

let private choose (commentId: string) (action: Action) (rows: Row list) =
  match find commentId rows with
  | Some({ State = Progress.Ready | Progress.NotDone _ } as row) when List.contains action (writerActions row.Comment.Comment.Writer) ->
    let effect =
      match action, row.Comment.Comment.Writer with
      | ApproveAndTrust, Api.Writer.Commenter commenter -> Some(Trust(row.Comment, commenter))
      | ApproveAndTrust, Api.Writer.Author -> None
      | (Approve | Reject | Delete), _ -> Some(Moderate(row.Comment, action))

    match effect with
    | Some _ -> Loaded(change commentId (Progress.Sending action) rows), effect
    | None -> Loaded rows, None
  | Some _
  | None -> Loaded rows, None

let private trusted (commentId: string) (rows: Row list) =
  match find commentId rows |> Option.bind (fun row -> commenterId row |> Option.map (fun trustedId -> row, trustedId)) with
  | Some(row, trustedId) ->
    let trust = withTrust trustedId
    Loaded(rows |> List.map trust), Some(Moderate((trust row).Comment, Approve))
  | None -> Loaded rows, None

/// Each approved comment is no longer new, so it leaves the queue, and each reply under it gets the approval.
let private approved (commentIds: string list) (rows: Row list) =
  rows
  |> List.filter (fun row -> not (List.contains (id row) commentIds))
  |> List.map (fun row ->
    { row with
        Comment.Comment = ReplyContext.withTopLevelApproval commentIds Api.CommentApproval.Approved row.Comment.Comment })

let private moderated (commentId: string) (action: Action) (rows: Row list) =
  match action with
  | Approve
  | ApproveAndTrust -> approved [ commentId ] rows
  | Reject ->
    rows
    |> List.filter (fun row -> id row <> commentId)
    |> List.map (fun row ->
      { row with
          Comment.Comment = ReplyContext.withTopLevelApproval [ commentId ] Api.CommentApproval.Rejected row.Comment.Comment })
  | Delete ->
    rows
    |> ReplyContext.rowsAfterDelete _.Comment.Comment (fun comment row -> { row with Comment.Comment = comment }) commentId
    // A delete on the comments of a post can remove a comment that is not in the queue, with its replies.
    |> Option.defaultWith (fun () -> rows |> List.filter (fun row -> row.Comment.Comment.ReplyTo <> Some commentId))

let private reply (msg: AuthorReply.Msg) (model: Model) : Model * Effect option =
  match model.Comments, msg with
  | Loaded rows, AuthorReply.Opened commentId ->
    match find commentId rows with
    | Some row when not (AuthorReply.isOpenOn commentId model.Reply) ->
      let comment = row.Comment.Comment
      { model with Reply = AuthorReply.opened (ReplyContext.topLevelApproval comment) comment }, None
    | Some _
    | None -> model, None
  | Loaded rows, AuthorReply.Answered(commentId, Ok answer) when AuthorReply.isOpenOn commentId model.Reply ->
    { model with
        Comments = Loaded(approved answer.Approved rows)
        Reply = None },
    None
  | _ ->
    let form, effect = AuthorReply.update msg model.Reply

    let effect =
      match effect, model.Comments with
      | Some(AuthorReply.Preview(commentId, request)), _ -> Some(PreviewReply(commentId, request))
      | Some(AuthorReply.Post(commentId, content)), Loaded rows ->
        find commentId rows |> Option.map (fun row -> PostReply(row.Comment, content))
      | Some(AuthorReply.Post _), (Waiting | NotLoaded _)
      | None, _ -> None

    { model with Reply = form }, effect

let private withRows (rows: Comments * Effect option) (model: Model) =
  let comments, effect = rows
  { model with Comments = comments }, effect

let update (msg: Msg) (model: Model) : Model * Effect option =
  match model.Comments, msg with
  | _, Answered(Ok comments) ->
    { model with
        Comments = Loaded(comments |> List.map (fun comment -> { Comment = comment; State = Progress.Ready }))
        Reply = model.Reply |> AuthorReply.keepIf (fun commentId -> comments |> List.exists (fun comment -> comment.Comment.Id = commentId)) },
    None
  | _, Answered(Error failure) ->
    { model with
        Comments = NotLoaded(Api.reason failure)
        Reply = None },
    None
  | Loaded rows, Chosen(commentId, action) -> model |> withRows (choose commentId action rows)
  | Loaded rows, Trusted(commentId, Ok()) -> model |> withRows (trusted commentId rows)
  | Loaded rows, Trusted(commentId, Error failure) ->
    { model with Comments = Loaded(change commentId (Progress.NotDone(ApproveAndTrust, Api.reason failure)) rows) }, None
  | Loaded rows, Done(commentId, action, Ok()) ->
    { model with
        Comments = Loaded(moderated commentId action rows)
        Reply = model.Reply |> AuthorReply.keepIf ((<>) commentId) },
    None
  | Loaded rows, Done(commentId, action, Error failure) ->
    { model with Comments = Loaded(change commentId (Progress.NotDone(action, Api.reason failure)) rows) }, None
  | _, Reply msg -> reply msg model
  | _, TopLevelToggled commentId -> { model with Opened = ReplyContext.toggle commentId model.Opened }, None
  | (Waiting | NotLoaded _), (Chosen _ | Trusted _ | Done _) -> model, None

/// Removes each comment that a reply on another page approved.
let approvedElsewhere (commentIds: string list) (model: Model) =
  match model.Comments with
  | Loaded rows ->
    { model with
        Comments = Loaded(approved commentIds rows)
        Reply = model.Reply |> AuthorReply.keepIf (fun commentId -> not (List.contains commentId commentIds)) }
  | Waiting
  | NotLoaded _ -> model

let count (model: Model) =
  match model.Comments with
  | Loaded rows -> Some(List.length rows)
  | Waiting
  | NotLoaded _ -> None

let tryFind (commentId: string) (model: Model) =
  match model.Comments with
  | Loaded rows -> find commentId rows
  | Waiting
  | NotLoaded _ -> None

/// The body of the confirm dialog of a comment delete. `replyCount` is the number of replies that go with it.
let deleteText (replyCount: int) =
  let thread (replies: string) =
    $"This deletes the comment, {replies}, and their history. To hide the thread from readers but keep it, reject the comment instead. This can't be undone."

  match replyCount with
  | 0 ->
    "This deletes the comment and its history. To hide it from readers but keep it, reject it instead. This can't be undone."
  | 1 -> thread "its reply"
  | count -> thread $"its {count} replies"

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
