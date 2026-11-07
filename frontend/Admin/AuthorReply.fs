[<RequireQualifiedAccess>]
module TwoPoint.Admin.AuthorReply

open TwoPoint.Site.Widgets

type Posting =
  | Editing
  | Posting
  | NotPosted of reason: string

type Form =
  { CommentId: string
    Editor: Comments.Editor
    Posting: Posting }

type Msg =
  | Opened of commentId: string
  | Canceled
  | Edited of commentId: string * Comments.Edit
  | Submitted
  | Answered of commentId: string * Result<Api.AuthorReply, Api.Failure>

type Effect =
  | Preview of commentId: string * Comments.PreviewRequest
  | Post of commentId: string * content: string

/// Readers can't see a rejected comment or a reply under it, so neither gets a reply.
/// `topLevel` is the approval of the top-level comment of a reply, when the page has that comment.
let canReply (topLevel: Api.CommentApproval option) (approval: Api.CommentApproval) =
  match approval, topLevel with
  | Api.CommentApproval.Rejected, _
  | _, Some Api.CommentApproval.Rejected -> false
  | (Api.CommentApproval.New | Api.CommentApproval.Approved),
    (None | Some(Api.CommentApproval.New | Api.CommentApproval.Approved)) -> true

let actionLabel (approval: Api.CommentApproval) =
  match approval with
  | Api.CommentApproval.New -> "Reply and approve"
  | Api.CommentApproval.Approved
  | Api.CommentApproval.Rejected -> "Reply"

/// The backend approves a new comment, and its new top-level comment, when the author replies to it.
let approvalNote (approval: Api.CommentApproval) =
  match approval with
  | Api.CommentApproval.New -> Some "Posting your reply also approves this comment."
  | Api.CommentApproval.Approved
  | Api.CommentApproval.Rejected -> None

let opened (topLevel: Api.CommentApproval option) (comment: Api.Comment) =
  if canReply topLevel comment.Status then
    Some
      { CommentId = comment.Id
        Editor = Comments.emptyEditor
        Posting = Editing }
  else
    None

let canPost (form: Form) =
  form.Posting <> Posting
  && Comments.isValidComment form.Editor.Text
  && form.Editor.Text.Length <= Comments.maxLength

let failure (form: Form) =
  match form.Posting with
  | NotPosted reason -> Some $"Couldn't post the reply. {reason}"
  | Editing
  | Posting -> None

let private edited (change: Comments.Edit) (form: Form) =
  match form.Posting, change with
  | Posting, Comments.TextChanged _ -> Some form, None
  | _ ->
    let editor, request = Comments.edit change form.Editor
    Some { form with Editor = editor }, request |> Option.map (fun request -> Preview(form.CommentId, request))

/// `Opened` needs the comment of the row, so the page handles it, and an answer that posted the reply.
let update (msg: Msg) (form: Form option) : Form option * Effect option =
  match form, msg with
  | Some form, Canceled when form.Posting <> Posting -> None, None
  | Some form, Edited(commentId, change) when commentId = form.CommentId -> edited change form
  | Some form, Submitted when canPost form ->
    Some { form with Posting = Posting }, Some(Post(form.CommentId, form.Editor.Text))
  | Some form, Answered(commentId, Error failure) when commentId = form.CommentId ->
    Some { form with Posting = NotPosted(Api.reason failure) }, None
  | _, Opened _
  | _, Canceled
  | _, Edited _
  | _, Submitted
  | _, Answered _ -> form, None

let isOpenOn (commentId: string) (form: Form option) =
  form |> Option.exists (fun form -> form.CommentId = commentId)

/// Closes the form when its comment is no longer in the list.
let keepIf (isListed: string -> bool) (form: Form option) =
  form |> Option.filter (fun form -> isListed form.CommentId)
