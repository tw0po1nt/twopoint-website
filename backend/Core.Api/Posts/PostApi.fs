namespace TwoPoint.Core.Posts.Api

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Logic
open TwoPoint.Core.Posts.Dependencies
open TwoPoint.Core.Util

open IcedTasks

type IPostApi =
  abstract member CreatePost: newPost: NewPostDto -> CancellableTask<ApiResult<PostEvent list, CreatePostError>>
  abstract member PostComment: newComment: NewCommentDto -> CancellableTask<ApiResult<PostEvent list, PostCommentError>>
  abstract member UpdateCommentApproval: CommentApprovalUpdateDto -> CancellableTask<ApiResult<PostEvent list, UpdateCommentApprovalError>>
  abstract member UpdateCommenterStatus: CommenterStatusUpdateDto -> CancellableTask<ApiResult<PostEvent list, UpdateCommenterStatusError>>

[<RequireQualifiedAccess>]
module PostApi =
  
  open FsToolkit.ErrorHandling
  
  let withDependencies (postDependencies: IPostDependencies) =
    
    let handleCommentPosted (CommentPosted (post, comment, commenter)) : DependencyResult<Comment> =
      cancellableTaskResult {
        let! (commenter: Commenter) = cancellableTaskResult {
          match commenter with
          | Some c -> return c
          | None -> return! postDependencies.CreateCommenter comment.EmailAddress comment.Name CommenterStatus.New
        }
          
        return! postDependencies.CreateComment post commenter comment
      }
      
    let handleCommentApprovalUpdated (CommentApprovalUpdated (comment, approval)) : DependencyResult<Comment> =
      cancellableTaskResult {
        
        do! cancellableTaskResult {
          match approval with
          | Approved ->
            return!
              CommenterStatus.Trusted
              |> postDependencies.UpdateCommenterStatus comment.Commenter
              |> CancellableTaskResult.ignore
          | _ -> return ()
        }
        
        return!
          postDependencies.UpdateCommentApproval comment approval
          |> CancellableTaskResult.map (fun comment ->
            match approval with
            | Approved -> { comment with Comment.Commenter.Status = CommenterStatus.Trusted }
            | _ -> comment
          )
      }
      
    let handleCommenterStatusUpdated (CommenterStatusUpdated (commenter, status)) : DependencyResult<Commenter> =
      cancellableTaskResult {
        // todo: call code to reject all comments from commenter if banned
        
        return! postDependencies.UpdateCommenterStatus commenter status
      }
    
    { new IPostApi with
    
      member this.CreatePost newPost = cancellableTaskResult {
        let! (newPost: NewPost) =
          NewPost.create
            newPost.Title
            newPost.Slug
          |> Result.mapError ApiError<CreatePostError>.Validation
                      
        let! existingPost =
          postDependencies.GetPostBySlug newPost.Slug
          |> CancellableTaskResult.mapError ApiError<CreatePostError>.Dependency
          
        let decision = Posts.createPost existingPost newPost
          
        return!
          decision
          |> Result.map (fun (PostCreated newPost) -> newPost)
          |> Result.traverseCancellableTask postDependencies.CreatePost
          |> CancellableTaskResult.foldResult
            (DependencyResult.toApiResult PostEvent.PostCreated)
            (ApiError.Logic >> ApiResult.failure)
      }
        
      member this.PostComment newComment = cancellableTaskResult {
        let! (newComment: NewComment) =
          NewComment.create
            newComment.Post
            newComment.EmailAddress
            newComment.Name
            newComment.Comment
          |> Result.mapError ApiError<PostCommentError>.Validation
            
        let! existingPost =
          postDependencies.GetPostBySlug newComment.Post
          |> CancellableTaskResult.mapError ApiError<PostCommentError>.Dependency
          
        let! existingCommenter =
          postDependencies.GetCommenterByEmail newComment.EmailAddress
          |> CancellableTaskResult.mapError ApiError<PostCommentError>.Dependency
            
        let decision = Posts.postComment existingPost existingCommenter newComment
        
        let commentPostedEvent existingPost comment = PostEvent.CommentPosted (existingPost, comment)
        
        return!
          decision
          |> Result.traverseCancellableTask handleCommentPosted
          |> CancellableTaskResult.foldResult
            (DependencyResult.toApiResult (commentPostedEvent (Option.get existingPost)))
            (ApiError.Logic >> ApiResult.failure)
      }
      
      member this.UpdateCommentApproval approvalUpdate = cancellableTaskResult {
        let! (approvalUpdate: CommentApprovalUpdate) =
          CommentApprovalUpdate.create
            approvalUpdate.CommentId
            approvalUpdate.Approval
          |> Result.mapError ApiError<UpdateCommentApprovalError>.Validation
              
        let! existingComment =
          postDependencies.GetCommentById approvalUpdate.CommentId
          |> CancellableTaskResult.mapError ApiError<UpdateCommentApprovalError>.Dependency
          
        let decision = Posts.updateCommentApproval existingComment approvalUpdate
        
        return!
          decision
          |> Result.traverseCancellableTask handleCommentApprovalUpdated
          |> CancellableTaskResult.foldResult
            (DependencyResult.toApiResult PostEvent.CommentApprovalUpdated)
            (ApiError.Logic >> ApiResult.failure)
      }
      
      member this.UpdateCommenterStatus statusUpdate = cancellableTaskResult {
        let! (statusUpdate: CommenterStatusUpdate) =
          CommenterStatusUpdate.create
            statusUpdate.EmailAddress
            statusUpdate.Status
          |> Result.mapError ApiError<UpdateCommenterStatusError>.Validation
              
        let! existingCommenter =
          postDependencies.GetCommenterByEmail statusUpdate.EmailAddress
          |> CancellableTaskResult.mapError ApiError<UpdateCommenterStatusError>.Dependency
          
        let decision = Posts.updateCommenterStatus existingCommenter statusUpdate
        
        return!
          decision
          |> Result.traverseCancellableTask handleCommenterStatusUpdated
          |> CancellableTaskResult.foldResult
            (DependencyResult.toApiResult PostEvent.CommenterStatusUpdated)
            (ApiError.Logic >> ApiResult.failure)
      }
    }
  
  
