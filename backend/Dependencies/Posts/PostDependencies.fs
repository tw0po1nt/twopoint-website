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

  let statsForApproval = function
    | "new" ->
      { New = 1u; Approved = 0u; Rejected = 0u }
    | "approved" ->
      { New = 0u; Approved = 1u; Rejected = 0u }
    | "rejected" ->
      { New = 0u; Approved = 0u; Rejected = 1u }
    | _ -> PostCommentStats.Zero

  let postDb = PostDb.live tableServiceClient logger
  let commsService = CommsService.live emailClient tableServiceClient logger emailSender

  let source = Dependency.Database

  let allApprovals = CommentApproval.all |> List.map _.ToString()

  let postedComments (readComments : CancellableTaskResult<(DbComment * DbCommenter * DbCommentStatus) list, DependencyError>) =
    cancellableTaskResult {
      let! dbPosts, dbComments = CancellableTaskResult.parallelZip (postDb.GetAllPosts()) readComments
      return!
        dbComments
        |> List.traverseValidationA (fun (comment: DbComment, commenter, status) ->
          let post = dbPosts |> List.tryFind (fun (post: DbPost) -> post.Slug = comment.PostId)
          comment.ToPostedComment(post, status, commenter))
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
        let stats =
          ({ New = 0u; Approved = 0u; Rejected = 0u }, comments)
          ||> List.fold (fun stats (_, _, status: DbCommentStatus) ->
            let updates = statsForApproval status.Approval
            stats + updates
          )
        post.ToPost(stats)
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

    let commentStats =
      ({ New = 0u; Approved = 0u; Rejected = 0u }, dbComments)
      ||> List.fold (fun stats (_, _, status: DbCommentStatus) ->
        let updates = statsForApproval status.Approval
        stats + updates
      )

    return!
      dbPost
      |> Option.traverseResult (fun (dbo: DbPost) -> dbo.ToPost(commentStats))
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
        fun (comment: DbComment, commenter, status) -> comment.ToComment(status, commenter)
      )
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
        fun (comment: DbComment, commenter, status) -> comment.ToComment(status, commenter)
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
      |> Option.traverseValidation (fun (comment: DbComment, commenter, status) -> comment.ToComment(status, commenter))
      |> DependencyError.ofValidation source
  }

  member _.CreateComment (post : Post) (commenter : Commenter) (newComment : NewComment) (approval : CommentApproval) = cancellableTaskResult {
    let newDbComment =
      { DbComment.Id = Guid.NewGuid().ToString() // todo: model as ISystemDependencies
        PostId = post.Id |> Slug.value
        CreatedDate = DateTime.UtcNow // todo: model as ISystemDependencies
        Content = newComment.Content |> NonEmptyString.value
        CommenterId = commenter.ValidationId.ToString() }
    let newDbStatus =
      { DbCommentStatus.Id = Guid.NewGuid().ToString() // todo: model as ISystemDependencies
        CommentId = newDbComment.Id
        CreatedDate = newDbComment.CreatedDate
        Approval = approval.ToString().ToLower() }

    let! (dbComment: DbComment, dbCommenter, dbCommentStatus) =
      postDb.CreateComment newDbComment newDbStatus

    return!
      dbComment.ToComment(dbCommentStatus, dbCommenter)
      |> DependencyError.ofValidation source
  }

  member _.GetPostedCommentById (commentId : CommentId) = cancellableTaskResult {
    let! dbComment = postDb.GetCommentById (commentId.ToString())

    match dbComment with
    | None -> return None
    | Some (comment : DbComment, commenter, status) ->
      let! dbPost = postDb.GetPostBySlug comment.PostId

      return!
        comment.ToPostedComment(dbPost, status, commenter)
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
