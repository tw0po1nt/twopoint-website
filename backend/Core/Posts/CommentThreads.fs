namespace TwoPoint.Core.Posts

open TwoPoint.Core.Shared

open System

[<RequireQualifiedAccess>]
type ReplyRefusal =
  | ReplyToReply
  | ReplyToNew
  | ReplyToRejected
  | ReplyToMissing
  | PostUpcoming

/// A reply has the comment that its `ReplyTo` names, or None when no comment has that ID.
[<RequireQualifiedAccess>]
type NewCommentPlace =
  | TopLevel
  | Reply of replyTo : PostedComment option

[<RequireQualifiedAccess>]
module ReplyCheck =

  /// A comment on another post is missing for this post.
  let check (utcNow : DateTime) (post : Post) (replyTo : PostedComment option) : Result<Comment, ReplyRefusal> =
    if post.Info.CreatedDate > utcNow then
      Error ReplyRefusal.PostUpcoming
    else
      match replyTo with
      | None -> Error ReplyRefusal.ReplyToMissing
      | Some replyTo when replyTo.Post <> post.Id -> Error ReplyRefusal.ReplyToMissing
      | Some { Comment = { Parent = Some _ } } -> Error ReplyRefusal.ReplyToReply
      | Some { Comment = topLevel } ->
        match topLevel.Status.Approval with
        | CommentApproval.New -> Error ReplyRefusal.ReplyToNew
        | CommentApproval.Rejected -> Error ReplyRefusal.ReplyToRejected
        | CommentApproval.Approved -> Ok topLevel

[<RequireQualifiedAccess>]
type AuthorReplyRefusal =
  | ReplyToRejected
  | ReplyToMissing

/// `Approvals` is each new comment that the reply approves, the top-level comment first.
type AuthorReplyPlace =
  { TopLevel : Comment
    Approvals : Comment list }

[<RequireQualifiedAccess>]
module AuthorReplyRule =

  let private isNew (comment : Comment) = comment.Status.Approval = CommentApproval.New

  let private isRejected (comment : Comment) = comment.Status.Approval = CommentApproval.Rejected

  /// `topLevel` is the top-level comment of `replyTo` when `replyTo` is a reply. A comment on another post is missing.
  /// Readers can't see a rejected comment or a reply under it, so the author can't reply to either.
  let place
    (post : Slug)
    (replyTo : PostedComment option)
    (topLevel : PostedComment option)
    : Result<AuthorReplyPlace, AuthorReplyRefusal> =
    match replyTo with
    | None -> Error AuthorReplyRefusal.ReplyToMissing
    | Some replyTo when replyTo.Post <> post -> Error AuthorReplyRefusal.ReplyToMissing
    | Some { Comment = comment } when isRejected comment -> Error AuthorReplyRefusal.ReplyToRejected
    | Some { Comment = comment } ->
      match comment.Parent, topLevel with
      | None, _ -> Ok { TopLevel = comment; Approvals = [ comment ] |> List.filter isNew }
      | Some parent, Some topLevel when topLevel.Comment.Id = parent && topLevel.Post = post ->
        if isRejected topLevel.Comment then
          Error AuthorReplyRefusal.ReplyToRejected
        else
          Ok { TopLevel = topLevel.Comment; Approvals = [ topLevel.Comment; comment ] |> List.filter isNew }
      | Some _, _ -> Error AuthorReplyRefusal.ReplyToMissing

[<RequireQualifiedAccess>]
module CommentVisibility =

  /// Readers see a reply only when the reply and its top-level comment are approved.
  let visible (comments : Comment list) : Comment list =
    let approved = comments |> List.filter (fun comment -> comment.Status.Approval = CommentApproval.Approved)

    let approvedTopLevel =
      approved |> List.filter (fun comment -> comment.Parent.IsNone) |> List.map _.Id |> Set.ofList

    approved
    |> List.filter (fun comment ->
      match comment.Parent with
      | None -> true
      | Some parent -> approvedTopLevel.Contains parent)

/// A comment as the public comments answer gives it. It has no email address.
type PublicComment =
  { Id : string
    CreatedDate : DateTime
    /// Empty for a comment from the author.
    Commenter : string
    Content : string
    ReplyTo : string option
    /// `commenter` or `author`.
    Writer : string }

[<RequireQualifiedAccess>]
module PublicComment =

  let ofRendered (rendered : RenderedComment) =
    let comment = rendered.Comment

    { Id = comment.Id.ToString()
      CreatedDate = comment.CreatedDate
      Commenter =
        match comment.Writer with
        | CommentWriter.Commenter commenter ->
          commenter.Name |> Option.map _.ToString() |> Option.defaultValue "Anonymous"
        | CommentWriter.Author -> ""
      Content = rendered.Html
      ReplyTo = comment.Parent |> Option.map _.ToString()
      Writer =
        match comment.Writer with
        | CommentWriter.Commenter _ -> "commenter"
        | CommentWriter.Author -> "author" }

type TopLevelComment =
  { Comment : RenderedComment
    Excerpt : string }

/// `TopLevel` is None for a top-level comment, and for a reply whose top-level comment is gone.
type AdminComment =
  { Comment : RenderedComment
    TopLevel : TopLevelComment option }

[<RequireQualifiedAccess>]
module AdminComment =

  /// The length of the excerpt in characters of the plain text.
  let excerptLength = 200

  let excerpt (comment : Comment) =
    comment.Content
    |> NonEmptyString.value
    |> TwoPoint.Site.Render.CommentBody.plainText
    |> _.EnumerateRunes()
    |> Seq.truncate excerptLength
    |> Seq.map _.ToString()
    |> String.concat ""

  /// `known` is each comment that can be the top-level comment of a comment in `comments`.
  let ofComments (stage : TwoPoint.Site.Render.Stage) (known : Comment list) (comments : Comment list) =
    let topLevels = known |> List.filter (fun comment -> comment.Parent.IsNone) |> List.map (fun comment -> comment.Id, comment) |> Map.ofList

    let topLevelOf (comment : Comment) =
      comment.Parent
      |> Option.bind topLevels.TryFind
      |> Option.map (fun topLevel -> { Comment = RenderedComment.ofComment stage topLevel; Excerpt = excerpt topLevel })

    comments
    |> List.sortBy _.CreatedDate
    |> List.map (fun comment -> { Comment = RenderedComment.ofComment stage comment; TopLevel = topLevelOf comment })

type AdminPostedComment =
  { Post : Slug
    PostTitle : NonEmptyString
    Comment : AdminComment }

[<RequireQualifiedAccess>]
module AdminPostedComment =

  /// `known` is each comment that can be the top-level comment of a comment in `comments`. The list is oldest first.
  let ofPostedComments (stage : TwoPoint.Site.Render.Stage) (known : Comment list) (comments : PostedComment list) =
    let postedById = comments |> List.map (fun posted -> posted.Comment.Id, posted) |> Map.ofList

    comments
    |> List.map _.Comment
    |> AdminComment.ofComments stage known
    |> List.map (fun comment ->
      let posted = postedById[comment.Comment.Comment.Id]
      { Post = posted.Post; PostTitle = posted.PostTitle; Comment = comment })

type CommenterComments =
  { Commenter : Commenter
    Comments : AdminPostedComment list }
