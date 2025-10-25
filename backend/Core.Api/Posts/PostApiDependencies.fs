namespace TwoPoint.Core.Posts.Dependencies

open TwoPoint.Core.Notifications.Dependencies
open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Logic
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FSharp.Control
open IcedTasks

open System
open System.Web

type IPostDependencies =
  abstract member GetAllPosts: unit -> DependencyResult<PostInfo list>
  abstract member GetPostBySlug: slug: Slug -> DependencyResult<Post option>
  abstract member CreatePost: newPost: NewPost -> DependencyResult<Post>
  
  abstract member GetCommenterByEmailAddress: emailAddress: EmailAddress -> DependencyResult<Commenter option>
  abstract member GetCommenterByValidationId: validationId: CommenterValidationId -> DependencyResult<Commenter option>
  abstract member CreateCommenter:
    emailAddress: EmailAddress
      -> name: NonEmptyString option
      -> status: CommenterStatus
      -> DependencyResult<Commenter>
  abstract member SendCommenterValidationEmail:
    commenter: Commenter
      -> redirectUri: Uri
      -> DependencyResult<OperationId>
  abstract member UpdateCommenterStatus:
    commenter: Commenter
      -> status: CommenterStatus
      -> DependencyResult<Commenter>
  
  abstract member GetCommentsForPost:
    approvals: CommentApproval list
      -> post: Post
      -> DependencyResult<Comment list>
  abstract member GetCommentById: commentId: CommentId -> DependencyResult<Comment option>
  abstract member CreateComment:
    post: Post
      -> commenter: Commenter
      -> newComment: NewComment
      -> DependencyResult<Comment>
  abstract member UpdateCommentApproval:
    comment: Comment
      -> approval: CommentApproval
      -> DependencyResult<Comment>
  abstract member DeleteComment: comment: Comment -> DependencyResult<unit>
      
  abstract member GetValidRedirectUris: unit -> DependencyResult<Uri list>
  
[<RequireQualifiedAccess>]
module PostDependencies =
  
  open TwoPoint.Core.Shared.Impl
  
  open FsToolkit.ErrorHandling
  
  let statsForApproval = function
    | "new" ->
      { New = 1u; Approved = 0u; Rejected = 0u }
    | "approved" ->
      { New = 0u; Approved = 1u; Rejected = 0u }
    | "rejected" ->
      { New = 0u; Approved = 0u; Rejected = 1u }
    | _ -> PostCommentStats.Zero

  let live validRedirectUris emailClient emailSender messaging tableServiceClient logger =
    let postDb = PostDb.live tableServiceClient logger
    let notificationDb = NotificationDb.live tableServiceClient logger
    let commsService = CommsService.live emailClient messaging tableServiceClient logger emailSender
    
    let source = Dependency.Database
    
    let allApprovals = CommentApproval.all |> List.map _.ToString()
    
    { new IPostDependencies with
      
      member this.GetAllPosts () = cancellableTaskResult {
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
      
      member this.GetPostBySlug slug = cancellableTaskResult {
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
      
      member this.CreatePost newPost = cancellableTaskResult {
        let newPost =
          { DbPost.Title = newPost.Title |> NonEmptyString.value
            Slug = newPost.Slug |> Slug.value
            CreatedDate = DateTime.UtcNow } // todo: model as ISystemDependencies
        let! (dbPost: DbPost) = postDb.CreatePost newPost
        return! dbPost.ToPost(PostCommentStats.Zero) |> DependencyError.ofValidation source
      }
      
      member this.GetCommenterByEmailAddress emailAddress = cancellableTaskResult {
        let! dbCommenter = postDb.GetCommenterByEmailAddress (emailAddress.ToString())

        return!
          dbCommenter
          |> Option.traverseResult (fun (dbo: DbCommenter) -> dbo.ToCommenter())
          |> DependencyError.ofValidation source
      }
      
      member this.GetCommenterByValidationId validationId = cancellableTaskResult {
        let! dbCommenter = postDb.GetCommenterByValidationId (validationId.ToString())

        return!
          dbCommenter
          |> Option.traverseResult (fun (dbo: DbCommenter) -> dbo.ToCommenter())
          |> DependencyError.ofValidation source
      }
      
      member this.CreateCommenter emailAddress name status = cancellableTaskResult {
        let newCommenter =
          { ValidationId = Guid.NewGuid().ToString() // todo: model as ISystemDependencies
            DbCommenter.EmailAddress = emailAddress |> EmailAddress.value
            Name = name |> Option.map NonEmptyString.value
            CreatedDate = DateTime.UtcNow // todo: model as ISystemDependencies
            Status = status.ToString().ToLower() } 
        
        let! (dbCommenter: DbCommenter) = postDb.CreateCommenter newCommenter
        return! dbCommenter.ToCommenter() |> DependencyError.ofValidation source
      }
      
      member this.SendCommenterValidationEmail commenter redirectUri = cancellableTaskResult {
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
      
      member this.UpdateCommenterStatus commenter status = cancellableTaskResult {
        let dbCommenter =
          { DbCommenter.ValidationId = commenter.ValidationId.ToString()
            EmailAddress = commenter.EmailAddress |> EmailAddress.value
            Name = commenter.Name |> Option.map NonEmptyString.value
            CreatedDate = commenter.CreatedDate
            Status = commenter.Status.ToString().ToLower() }
        let dbStatus = status.ToString().ToLower()
        let! (dbCommenter: DbCommenter) = postDb.UpdateCommenterStatus dbCommenter dbStatus
        return! dbCommenter.ToCommenter() |> DependencyError.ofValidation source
      }
      
      member this.GetCommentsForPost approvals post = cancellableTaskResult {
        let! dbComments = postDb.GetCommentsForPost (approvals |> List.map _.ToString()) (DbPost.FromPost post)
        return!
          dbComments
          |> List.traverseValidationA (
            fun (comment: DbComment, commenter, status) -> comment.ToComment(status, commenter)
          )
          |> DependencyError.ofValidation source
      }
      
      member this.GetCommentById commentId = cancellableTaskResult {
        let! dbComment = postDb.GetCommentById (commentId.ToString())
        
        return!
          dbComment
          |> Option.traverseValidation (fun (comment: DbComment, commenter, status) -> comment.ToComment(status, commenter))
          |> DependencyError.ofValidation source
      }
      
      member this.CreateComment post commenter newComment = cancellableTaskResult {
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
            Approval = CommentApproval.New.ToString().ToLower() }
        
        let! (dbComment: DbComment, dbCommenter, dbCommentStatus) =
          postDb.CreateComment newDbComment newDbStatus
        
        // Notify admin
        do! cancellableTask {
          // todo: probably good to eventually not hardcode this admin/client lookup
          //       maybe would even be good to have this post an event and have the dispatching be handled separately
          //       this'll do for now
          let! notification =
            notificationDb.GetDeviceRegistrationByUserAndClient "ad1209ae-c4cc-4360-953a-ccbab9d68c83" "android"
            |> CancellableTaskResult.map (
              fun registration -> option {
                let! registration = registration
                let commenterName =
                  commenter.Name |> Option.map _.ToString() |> Option.defaultValue (commenter.EmailAddress.ToString())
                return
                  { Title = NonEmptyString.Unsafe.create None "Someone posted a new comment!"
                    Body = NonEmptyString.Unsafe.create None $"{commenterName} left a comment on the post '{post.Info.Title}'"
                    Recipient = NonEmptyString.Unsafe.create None registration.Token }
              }
            )
            |> CancellableTask.map (Result.defaultValue None)
            
          return!
            notification
            |> Option.traverseCancellableTaskResult commsService.SendPushNotification
            |> CancellableTaskResult.ignore
        }
        
        return!
          dbComment.ToComment(dbCommentStatus, dbCommenter)
          |> DependencyError.ofValidation source
      }
      
      member this.UpdateCommentApproval comment approval = cancellableTaskResult {
        let newStatus =
          { DbCommentStatus.Id = Guid.NewGuid().ToString() // todo: model as ISystemDependencies
            CommentId = comment.Id.ToString()
            CreatedDate = DateTime.UtcNow // todo: model as ISystemDependencies
            Approval = approval.ToString().ToLower() }
          
        let! (dbCommentStatus: DbCommentStatus) = postDb.UpdateCommentStatus newStatus
        let! status = dbCommentStatus.ToCommentStatus() |> DependencyError.ofValidation source
        return { comment with Status = status }
      }
      
      member this.DeleteComment comment = comment.Id.ToString() |> postDb.DeleteComment
      
      member this.GetValidRedirectUris () = CancellableTaskResult.singleton validRedirectUris
    }



