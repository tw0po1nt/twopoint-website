[<RequireQualifiedAccess>]
module TwoPoint.Admin.ReplyContext

let private possessive (writer: Api.Writer) =
  match writer with
  | Api.Writer.Commenter commenter -> $"{commenter.Name}'s"
  | Api.Writer.Author -> "your"

let replyLine (topLevel: Api.TopLevel) =
  match topLevel.Writer with
  | Api.Writer.Commenter commenter -> $"Reply to {commenter.Name}"
  | Api.Writer.Author -> "Reply to you"

/// Readers see a reply only when its top-level comment is approved too.
let rejectedNote (topLevel: Api.TopLevel) =
  match topLevel.Status with
  | Api.CommentApproval.Rejected ->
    Some $"Readers won't see this reply, because {possessive topLevel.Writer} comment is rejected."
  | Api.CommentApproval.New
  | Api.CommentApproval.Approved -> None

let topLevelApproval (comment: Api.Comment) =
  comment.TopLevel |> Option.map _.Status

/// Each reply row whose top-level comment shows its full text.
type Opened = Set<string>

let toggle (commentId: string) (opened: Opened) : Opened =
  if opened.Contains commentId then opened.Remove commentId else opened.Add commentId

let withTopLevelApproval (topLevelIds: string list) (approval: Api.CommentApproval) (comment: Api.Comment) =
  match comment.TopLevel with
  | Some topLevel when List.contains topLevel.Id topLevelIds ->
    { comment with
        TopLevel = Some { topLevel with Status = approval } }
  | Some _
  | None -> comment

/// The comment after the delete of `deleted`, or None when it goes with it. A top-level comment takes its replies,
/// and a reply leaves its top-level comment with one reply fewer.
let afterDelete (deleted: Api.Comment) (comment: Api.Comment) =
  if comment.Id = deleted.Id || comment.ReplyTo = Some deleted.Id then
    None
  elif deleted.ReplyTo = Some comment.Id then
    Some { comment with ReplyCount = comment.ReplyCount - 1 }
  else
    Some comment

/// The comment after a new reply: its top-level comment has one reply more.
let afterReply (reply: Api.Comment) (comment: Api.Comment) =
  if reply.ReplyTo = Some comment.Id then
    { comment with ReplyCount = comment.ReplyCount + 1 }
  else
    comment
