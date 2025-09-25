namespace TwoPoint.Core.Posts.Dependencies

open FSharp.Control
open IcedTasks
open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Logic
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open System

type IPostDependencies =
  abstract member GetAllPosts: unit -> DependencyResult<PostInfo list>
  abstract member GetPostBySlug: slug: Slug -> DependencyResult<Post option>
  abstract member CreatePost: newPost: NewPost -> DependencyResult<Post>
  
  abstract member GetCommenterByValidationId: validationId: CommenterValidationId -> DependencyResult<Commenter option>
  abstract member CreateCommenter:
    emailAddress: EmailAddress
      -> name: NonEmptyString option
      -> status: CommenterStatus
      -> DependencyResult<Commenter>
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
  
[<RequireQualifiedAccess>]
module PostDependencies =
  
  open FsToolkit.ErrorHandling
  
  let statsForApproval = function
    | "new" ->
      { New = 1u; Approved = 0u; Rejected = 0u }
    | "approved" ->
      { New = 0u; Approved = 1u; Rejected = 0u }
    | "rejected" ->
      { New = 0u; Approved = 0u; Rejected = 1u }
    | _ -> PostCommentStats.Zero

  let live tableServiceClient logger =
    let postDb = PostDb.live tableServiceClient logger
    
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
      
      member this.UpdateCommenterStatus commenter status = cancellableTaskResult {
        let dbCommenter =
          { DbCommenter.ValidationId = commenter.ValidationId.ToString()
            EmailAddress = commenter.EmailAddress |> EmailAddress.value
            Name = commenter.Name |> Option.map NonEmptyString.value
            CreatedDate = commenter.CreatedDate // todo: model as ISystemDependencies
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
        let newComment =
          { DbComment.Id = Guid.NewGuid().ToString() // todo: model as ISystemDependencies
            PostId = post.Id |> Slug.value
            CreatedDate = DateTime.UtcNow // todo: model as ISystemDependencies
            Content = newComment.Content |> NonEmptyString.value
            CommenterId = commenter.ValidationId.ToString() }
        let newStatus =
          { DbCommentStatus.Id = Guid.NewGuid().ToString() // todo: model as ISystemDependencies
            CommentId = newComment.Id
            CreatedDate = newComment.CreatedDate
            Approval = CommentApproval.New.ToString().ToLower() }
        
        let! (dbComment: DbComment, dbCommenter, dbCommentStatus) =
          postDb.CreateComment newComment newStatus
        
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
    }
