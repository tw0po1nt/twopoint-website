namespace TwoPoint.Core.Posts.Dependencies

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Logic
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open System

type IPostDependencies =
  abstract member GetPostBySlug: slug: Slug -> DependencyResult<Post option>
  abstract member CreatePost: newPost: NewPost -> DependencyResult<Post>
  
  abstract member GetCommenterByEmail: emailAddress: EmailAddress -> DependencyResult<Commenter option>
  abstract member CreateCommenter:
    emailAddress: EmailAddress
      -> name: NonEmptyString option
      -> status: CommenterStatus
      -> DependencyResult<Commenter>
  abstract member UpdateCommenterStatus:
    commenter: Commenter
      -> status: CommenterStatus
      -> DependencyResult<Commenter>
  
  abstract member GetCommentsForPost: post: Post -> DependencyResult<Comment list>
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

  let live tableServiceClient logger =
    let postDb = PostDb.live tableServiceClient logger
    
    let source = Dependency.Database
    
    { new IPostDependencies with
      member this.GetPostBySlug slug = cancellableTaskResult {
        let! dbPost = postDb.GetPostBySlug (slug |> Slug.value)
                
        return!
          dbPost
          |> Option.traverseResult (fun (dbo: DbPost) -> dbo.ToPost())
          |> DependencyError.ofValidation source
      }
      
      member this.CreatePost newPost = cancellableTaskResult {
        let newPost =
          { DbPost.Title = newPost.Title |> NonEmptyString.value
            Slug = newPost.Slug |> Slug.value
            CreatedDate = DateTime.UtcNow } // todo: model as ISystemDependencies
        let! (dbPost: DbPost) = postDb.CreatePost newPost
        return! dbPost.ToPost() |> DependencyError.ofValidation source
      }
      
      member this.GetCommenterByEmail email = cancellableTaskResult {
        let! dbCommenter = postDb.GetCommenterByEmailAddress (email |> EmailAddress.value)
        
        return!
          dbCommenter
          |> Option.traverseResult (fun (dbo: DbCommenter) -> dbo.ToCommenter())
          |> DependencyError.ofValidation source
      }
      
      member this.CreateCommenter emailAddress name status = cancellableTaskResult {
        let newCommenter =
          { DbCommenter.EmailAddress = emailAddress |> EmailAddress.value
            Name = name |> Option.map NonEmptyString.value
            CreatedDate = DateTime.UtcNow // todo: model as ISystemDependencies
            Status = status.ToString().ToLower() }
        
        let! (dbCommenter: DbCommenter) = postDb.CreateCommenter newCommenter
        return! dbCommenter.ToCommenter() |> DependencyError.ofValidation source
      }
      
      member this.UpdateCommenterStatus commenter status = cancellableTaskResult {
        let dbCommenter =
          { DbCommenter.EmailAddress = commenter.EmailAddress |> EmailAddress.value
            Name = commenter.Name |> Option.map NonEmptyString.value
            CreatedDate = commenter.CreatedDate // todo: model as ISystemDependencies
            Status = commenter.Status.ToString().ToLower() }
        let dbStatus = status.ToString().ToLower()
        let! (dbCommenter: DbCommenter) = postDb.UpdateCommenterStatus dbCommenter dbStatus
        return! dbCommenter.ToCommenter() |> DependencyError.ofValidation source
      }
      
      member this.GetCommentsForPost post = cancellableTaskResult {
        let! dbComments = postDb.GetCommentsForPost (DbPost.FromPost post)
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
            PostId = post.Slug |> Slug.value
            CreatedDate = DateTime.UtcNow // todo: model as ISystemDependencies
            Content = newComment.Content |> NonEmptyString.value
            CommenterId = commenter.EmailAddress |> EmailAddress.value }
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
