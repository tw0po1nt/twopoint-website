namespace TwoPoint.Dependencies.Posts

open TwoPoint.Dependencies.Comms
open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open Azure.Communication.Email
open Azure.Data.Tables
open FSharp.Control
open FsToolkit.ErrorHandling
open IcedTasks
open Microsoft.Extensions.Logging

open System
open System.Web

type EmailAddress = TwoPoint.Core.Shared.EmailAddress
type EmailMessage = TwoPoint.Dependencies.Comms.EmailMessage

type PostDependencies(
  validRedirectUris : Uri list,
  emailClient : EmailClient,
  emailSender : EmailAddress,
  tableServiceClient : TableServiceClient,
  logger : ILogger
) =

  let statsOf (dbComments : (DbComment * DbCommentWriter * DbCommentStatus) list) =
    dbComments
    |> List.choose (fun (comment, writer, status) ->
      match comment.ToComment(status, writer) with
      | Ok comment -> Some comment
      | Error errors ->
        logger.LogWarning(
          "Comment '{commentId}' is not valid ({errors}). The comment counts leave it out.",
          comment.Id,
          String.Join("; ", errors)
        )
        None)
    |> PostCommentStats.OfComments

  let postDb = PostDb.live tableServiceClient logger
  let commsService = CommsService.live emailClient tableServiceClient logger emailSender

  let source = Dependency.Database

  let allApprovals = CommentApproval.all |> List.map _.ToString()

  let postedComments (readComments : CancellableTaskResult<(DbComment * DbCommentWriter * DbCommentStatus) list, DependencyError>) =
    cancellableTaskResult {
      let! dbPosts, dbComments = CancellableTaskResult.parallelZip (postDb.GetAllPosts()) readComments
      return!
        dbComments
        |> List.traverseValidationA (fun (comment: DbComment, writer, status) ->
          let post = dbPosts |> List.tryFind (fun (post: DbPost) -> post.Slug = comment.PostId)
          comment.ToPostedComment(post, status, writer))
        |> DependencyError.ofValidation source
    }

  member _.GetAllPosts () = cancellableTaskResult {
    let! (dbPosts: DbPost list) = postDb.GetAllPosts()
    let! ct = CancellableTask.getCancellationToken()
    // TODO: Explore Task.WhenAll()...might work here and benefit from parallelism
    let dbPostsAndComments = taskSeq {
      for post in dbPosts do
        let! dbComments = (post, ct) ||> postDb.GetCommentsForPost allApprovals |> Task.map (Result.defaultValue [])
        yield post, dbComments
    }

    return!
      ([], dbPostsAndComments)
      ||> TaskSeq.fold (fun s (post, comments) ->
        post.ToPost(statsOf comments)
        |> Result.toOption
        |> Option.map (fun p -> p.Info :: s)
        |> Option.defaultValue s
      )
  }

  member _.GetPostBySlug slug = cancellableTaskResult {
    let! dbPost = postDb.GetPostBySlug (slug |> Slug.value)
    let! dbComments =
      dbPost
      |> Option.traverseCancellableTaskResult (postDb.GetCommentsForPost allApprovals)
      |> CancellableTaskResult.map (Option.defaultValue [])

    return!
      dbPost
      |> Option.traverseResult (fun (dbo: DbPost) -> dbo.ToPost(statsOf dbComments))
      |> DependencyError.ofValidation source
  }

  member _.UpsertPost (newPost : PostRow) = cancellableTaskResult {
    let newPost =
      { DbPost.Title = newPost.Title |> NonEmptyString.value
        Slug = newPost.Slug |> Slug.value
        CreatedDate = newPost.CreatedDate }
    let! (dbPost: DbPost) = postDb.UpsertPost newPost
    return! dbPost.ToPost(PostCommentStats.Zero) |> DependencyError.ofValidation source
  }

  member _.DeletePost (slug : Slug) = postDb.DeletePost(Slug.value slug)

  member _.DeleteCommentsOfPost (slug : Slug) = postDb.DeleteCommentsOfPost(Slug.value slug)

  member _.HasPostRecords (slug : Slug) = cancellableTaskResult {
    let! row = postDb.GetPostBySlug(Slug.value slug)
    return!
      match row with
      | Some _ -> CancellableTaskResult.singleton true
      | None -> postDb.HasCommentsOfPost(Slug.value slug)
  }

  member _.GetAllCommenters () = cancellableTaskResult {
    let! dbCommenters = postDb.GetAllCommenters()
    return!
      dbCommenters
      |> List.traverseValidationA (fun (dbo: DbCommenter) -> dbo.ToCommenter())
      |> DependencyError.ofValidation source
  }

  member _.GetCommenterByEmailAddress emailAddress = cancellableTaskResult {
    let! dbCommenter = postDb.GetCommenterByEmailAddress (emailAddress.ToString())

    return!
      dbCommenter
      |> Option.traverseResult (fun (dbo: DbCommenter) -> dbo.ToCommenter())
      |> DependencyError.ofValidation source
  }

  member _.GetCommenterByValidationId validationId = cancellableTaskResult {
    let! dbCommenter = postDb.GetCommenterByValidationId (validationId.ToString())

    return!
      dbCommenter
      |> Option.traverseResult (fun (dbo: DbCommenter) -> dbo.ToCommenter())
      |> DependencyError.ofValidation source
  }

  member _.CreateCommenter emailAddress name status = cancellableTaskResult {
    let newCommenter =
      { ValidationId = Guid.NewGuid().ToString() // todo: model as ISystemDependencies
        DbCommenter.EmailAddress = emailAddress |> EmailAddress.value
        Name = name |> Option.map NonEmptyString.value
        CreatedDate = DateTime.UtcNow // todo: model as ISystemDependencies
        Status = status.ToString().ToLower() } 

    let! (dbCommenter: DbCommenter) = postDb.CreateCommenter newCommenter
    return! dbCommenter.ToCommenter() |> DependencyError.ofValidation source
  }

  member _.SendCommenterValidationEmail (commenter : Commenter) (redirectUri : Uri) = cancellableTaskResult {
    let builder = UriBuilder redirectUri
    let query = HttpUtility.ParseQueryString builder.Query
    query["commenterValidationId"] <- commenter.ValidationId.ToString()
    builder.Query <- query.ToString()
    let redirectUri = builder.Uri;

    let subject, message =
      commenter.Name
      |> Option.map _.ToString()
      |> Option.defaultValue "there"
      |> PostEmails.verifyEmail redirectUri
    let recipient = commenter.EmailAddress.ToString()

    let! (message: EmailMessage) =
      EmailMessage.create
        subject
        message
        recipient
      |> DependencyError.ofValidation source

    return! commsService.SendEmail message  
  }

  member _.UpdateCommenterStatus (commenter : Commenter) status = cancellableTaskResult {
    let dbStatus = status.ToString().ToLower()
    let! (dbCommenter: DbCommenter) = postDb.UpdateCommenterStatus (DbCommenter.FromCommenter commenter) dbStatus
    return! dbCommenter.ToCommenter() |> DependencyError.ofValidation source
  }

  member _.GetCommentsForPost approvals post = cancellableTaskResult {
    let! dbComments = postDb.GetCommentsForPost (approvals |> List.map _.ToString()) (DbPost.FromPost post)
    return!
      dbComments
      |> List.traverseValidationA (
        fun (comment: DbComment, writer, status) -> comment.ToComment(status, writer)
      )
      |> DependencyError.ofValidation source
  }

  /// Each reply under one of the top-level comments, with each approval.
  member _.GetRepliesOf (topLevels : PostedComment list) = cancellableTaskResult {
    let! dbComments =
      topLevels
      |> List.map (fun (topLevel : PostedComment) -> Slug.value topLevel.Post, string topLevel.Comment.Id)
      |> postDb.GetRepliesOf allApprovals

    return!
      dbComments
      |> List.traverseValidationA (fun (comment: DbComment, writer, status) -> comment.ToComment(status, writer))
      |> DependencyError.ofValidation source
  }

  member _.GetCommentsOnAllPosts (approvals : CommentApproval list) =
    postDb.GetCommentsOnAllPosts (approvals |> List.map _.ToString()) |> postedComments

  member _.GetCommentsOfCommenter (commenter : Commenter) = cancellableTaskResult {
    let! dbComments =
      postDb.GetCommentsOfCommenter (CommentApproval.all |> List.map _.ToString()) (DbCommenter.FromCommenter commenter)
    return!
      dbComments
      |> List.traverseValidationA (
        fun (comment: DbComment, writer, status) -> comment.ToComment(status, writer)
      )
      |> DependencyError.ofValidation source
  }

  member _.GetPostedCommentsOfCommenter (commenter : Commenter) =
    postDb.GetCommentsOfCommenter allApprovals (DbCommenter.FromCommenter commenter) |> postedComments

  member _.DeleteCommenter (commenter : Commenter) = commenter.ValidationId.ToString() |> postDb.DeleteCommenter

  member _.GetCommentById commentId = cancellableTaskResult {
    let! dbComment = postDb.GetCommentById (commentId.ToString())

    return!
      dbComment
      |> Option.traverseValidation (fun (comment: DbComment, writer, status) -> comment.ToComment(status, writer))
      |> DependencyError.ofValidation source
  }

  member _.CreateComment
    (id : CommentId)
    (post : Post)
    (commenter : Commenter)
    (newComment : NewComment)
    (topLevel : Comment option)
    (approval : CommentApproval)
    (replyEmails : ReplyEmailConsent option)
    = cancellableTaskResult {
    let newDbComment =
      DbComment.OfCommenter(
        string id,
        DateTime.UtcNow, // todo: model as ISystemDependencies
        post.Id,
        commenter,
        newComment.Content,
        topLevel,
        replyEmails
      )
    let newDbStatus =
      { DbCommentStatus.Id = Guid.NewGuid().ToString() // todo: model as ISystemDependencies
        CommentId = newDbComment.Id
        CreatedDate = newDbComment.CreatedDate
        Approval = approval.ToString().ToLower() }

    let! (dbComment: DbComment, dbWriter, dbCommentStatus) =
      postDb.CreateComment newDbComment newDbStatus

    return!
      dbComment.ToComment(dbCommentStatus, dbWriter)
      |> DependencyError.ofValidation source
  }

  member _.CreateReplyEmailStop (stop : ReplyEmailStop) =
    postDb.CreateReplyEmailStop (DbReplyEmailStop.OfStop stop) |> CancellableTaskResult.ignore

  member _.GetReplyEmailStop (token : ReplyEmailStopToken) = cancellableTaskResult {
    let! (stop : DbReplyEmailStop option) = postDb.GetReplyEmailStop (token.ToString())

    return!
      stop
      |> Option.traverseValidation _.ToStop()
      |> DependencyError.ofValidation source
  }

  member _.UpdateReplyEmailConsent (post : Slug) (comment : CommentId) (consent : ReplyEmailConsent) =
    postDb.UpdateReplyEmailConsent (Slug.value post) (comment.ToString()) consent

  member _.GetReplyEmailRecord (post : Slug) (reply : CommentId) =
    postDb.GetReplyEmailSentDate (Slug.value post) (reply.ToString())
    |> CancellableTaskResult.map (function
      | Some sentDate -> ReplyEmailRecord.Sent sentDate
      | None -> ReplyEmailRecord.NotSent)

  member _.RecordReplyEmailSent (post : Slug) (reply : CommentId) (sentDate : DateTime) =
    postDb.RecordReplyEmailSent (Slug.value post) (reply.ToString()) sentDate

  member _.SendReplyEmail (recipient : EmailAddress) (content : ReplyEmailContent) = cancellableTaskResult {
    let! (message : EmailMessage) =
      EmailMessage.createWithPlainText content.Subject content.Html content.Text recipient
      |> DependencyError.ofValidation Dependency.Communications

    return! commsService.SendEmail message |> CancellableTaskResult.ignore
  }

  member _.CreateAuthorReply (post : Slug) (topLevel : Comment) (content : NonEmptyString) (approval : CommentApproval) = cancellableTaskResult {
    // TODO(https://github.com/tw0po1nt/twopoint-website-staging/issues/397): get the IDs and the time from an interface.
    let newDbComment =
      DbComment.OfAuthorReply(
        Guid.NewGuid().ToString(),
        DateTime.UtcNow,
        post,
        content,
        topLevel
      )
    let newDbStatus =
      { DbCommentStatus.Id = Guid.NewGuid().ToString()
        CommentId = newDbComment.Id
        CreatedDate = newDbComment.CreatedDate
        Approval = approval.ToString().ToLower() }

    let! (dbComment: DbComment, dbWriter, dbCommentStatus) =
      postDb.CreateComment newDbComment newDbStatus

    return!
      dbComment.ToComment(dbCommentStatus, dbWriter)
      |> DependencyError.ofValidation source
  }

  member _.GetPostedCommentById (commentId : CommentId) = cancellableTaskResult {
    let! dbComment = postDb.GetCommentById (commentId.ToString())

    match dbComment with
    | None -> return None
    | Some (comment : DbComment, writer, status) ->
      let! dbPost = postDb.GetPostBySlug comment.PostId

      return!
        comment.ToPostedComment(dbPost, status, writer)
        |> Result.map Some
        |> DependencyError.ofValidation source
  }

  member this.CountNewComments () =
    this.GetCommentsOnAllPosts [ CommentApproval.New ] |> CancellableTaskResult.map List.length

  member _.UpdateCommentApproval (comment : Comment) approval = cancellableTaskResult {
    let newStatus =
      { DbCommentStatus.Id = Guid.NewGuid().ToString() // todo: model as ISystemDependencies
        CommentId = comment.Id.ToString()
        CreatedDate = DateTime.UtcNow // todo: model as ISystemDependencies
        Approval = approval.ToString().ToLower() }

    let! (dbCommentStatus: DbCommentStatus) = postDb.UpdateCommentStatus newStatus
    let! status = dbCommentStatus.ToCommentStatus() |> DependencyError.ofValidation source
    return { comment with Status = status }
  }

  member _.DeleteComment (comment : Comment) = comment.Id.ToString() |> postDb.DeleteComment

  member _.GetValidRedirectUris () = CancellableTaskResult.singleton validRedirectUris
