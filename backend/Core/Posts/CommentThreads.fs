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

/// The comments that a delete removes, each reply before the top-level comments. A second try after a failed delete
/// then finds each top-level comment that is left, with its replies. `Others` is the number of comments that go with
/// the target of the delete.
type CommentRemoval =
  { Comments : Comment list
    Others : int }

[<RequireQualifiedAccess>]
module CommentRemoval =

  /// The order of `removal` stays, so each reply comes before its top-level comment.
  let exceptDeleted (deleted : CommentRemoval) (removal : CommentRemoval) : Comment list =
    let deletedIds = deleted.Comments |> List.map _.Id |> Set.ofList
    removal.Comments |> List.filter (fun comment -> not (deletedIds.Contains comment.Id))

/// The comments that a delete of a commenter removes, each with its post, each reply before the top-level comments.
type CommenterRemoval =
  { Comments : PostedComment list
    Others : int }

[<RequireQualifiedAccess>]
module CommenterRemoval =

  /// The order of `removal` stays, so each reply comes before its top-level comment.
  let exceptDeleted (deleted : CommenterRemoval) (removal : CommenterRemoval) : PostedComment list =
    let deletedIds = deleted.Comments |> List.map _.Comment.Id |> Set.ofList
    removal.Comments |> List.filter (fun posted -> not (deletedIds.Contains posted.Comment.Id))

[<RequireQualifiedAccess>]
module CommentDeleteRule =

  let private repliesFirst (comments : Comment list) =
    comments |> List.sortBy (fun comment -> comment.Parent.IsNone)

  /// A delete of a top-level comment removes its thread, also a reply from the author. A reply goes alone.
  /// `known` is each comment that can be a reply under `target`.
  let thread (known : Comment list) (target : Comment) : CommentRemoval =
    match target.Parent with
    | Some _ -> { Comments = [ target ]; Others = 0 }
    | None ->
      let replies = known |> List.filter (fun comment -> comment.Parent = Some target.Id) |> List.distinctBy _.Id
      { Comments = replies @ [ target ]; Others = replies.Length }

  /// A delete of a commenter removes each comment of the commenter and each thread that a comment of the commenter
  /// starts. A reply of the commenter in another thread goes alone. `Others` is the number of replies from other
  /// writers. `own` is each comment of the commenter, and `known` is each comment that can be a reply under a
  /// top-level comment of the commenter.
  let commenter (own : Comment list) (known : Comment list) : CommentRemoval =
    let ownIds = own |> List.map _.Id |> Set.ofList
    let threads = own |> List.filter (fun comment -> comment.Parent.IsNone) |> List.map _.Id |> Set.ofList

    let others =
      known
      |> List.filter (fun comment -> not (ownIds.Contains comment.Id) && comment.Parent |> Option.exists threads.Contains)
      |> List.distinctBy _.Id

    { Comments = repliesFirst (others @ own); Others = others.Length }

  /// `commenter` for the comments of the commenter on each post. A reply is on the post of its top-level comment.
  let commenterOnPosts (own : PostedComment list) (known : Comment list) : CommenterRemoval =
    let removals =
      own
      |> List.groupBy (fun posted -> posted.Post, posted.PostTitle)
      |> List.map (fun (post, posted) -> post, commenter (posted |> List.map _.Comment) known)

    { Comments =
        removals
        |> List.collect (fun ((post, title), removal) ->
          removal.Comments |> List.map (fun comment -> ({ Post = post; PostTitle = title; Comment = comment } : PostedComment)))
        |> List.sortBy _.Comment.Parent.IsNone
      Others = removals |> List.sumBy (snd >> _.Others) }

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
          Commenter.displayName commenter
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

/// `TopLevel` is None for a top-level comment, and for a reply whose top-level comment is gone. `ReplyCount` is the
/// number of replies that a delete of the comment removes with it.
type AdminComment =
  { Comment : RenderedComment
    TopLevel : TopLevelComment option
    ReplyCount : int }

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

  /// `known` is each comment that can be the top-level comment or a reply of a comment in `comments`.
  let ofComments (stage : TwoPoint.Site.Render.Stage) (known : Comment list) (comments : Comment list) =
    let topLevels = known |> List.filter (fun comment -> comment.Parent.IsNone) |> List.map (fun comment -> comment.Id, comment) |> Map.ofList

    let topLevelOf (comment : Comment) =
      comment.Parent
      |> Option.bind topLevels.TryFind
      |> Option.map (fun topLevel -> { Comment = RenderedComment.ofComment stage topLevel; Excerpt = excerpt topLevel })

    comments
    |> List.sortBy _.CreatedDate
    |> List.map (fun comment ->
      { Comment = RenderedComment.ofComment stage comment
        TopLevel = topLevelOf comment
        ReplyCount = (CommentDeleteRule.thread known comment).Others })

type AdminPostedComment =
  { Post : Slug
    PostTitle : NonEmptyString
    Comment : AdminComment }

[<RequireQualifiedAccess>]
module AdminPostedComment =

  /// `known` is each comment that can be the top-level comment or a reply of a comment in `comments`. The list is
  /// oldest first.
  let ofPostedComments (stage : TwoPoint.Site.Render.Stage) (known : Comment list) (comments : PostedComment list) =
    let postedById = comments |> List.map (fun posted -> posted.Comment.Id, posted) |> Map.ofList

    comments
    |> List.map _.Comment
    |> AdminComment.ofComments stage known
    |> List.map (fun comment ->
      let posted = postedById[comment.Comment.Comment.Id]
      { Post = posted.Post; PostTitle = posted.PostTitle; Comment = comment })

/// `OtherReplies` is the number of replies from other writers that a delete of the commenter removes.
type CommenterComments =
  { Commenter : Commenter
    Comments : AdminPostedComment list
    OtherReplies : int }
