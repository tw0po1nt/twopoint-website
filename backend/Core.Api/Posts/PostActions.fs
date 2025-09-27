namespace TwoPoint.Core.Posts.Api

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Logic
open TwoPoint.Core.Posts.Dependencies
open TwoPoint.Core.Util

open IcedTasks

// Inputs
type NewPostDto =
  { Title : string
    Slug : string }
  
type CommenterValidationDto =
  { EmailAddress : string
    Name : string option
    RedirectUri : string }
  
type NewCommentDto =
  { Post : string
    ValidationId : string
    Comment : string }
  
type CommentApprovalUpdateDto =
  { CommentId : string
    Approval : string }
  
type CommenterStatusUpdateDto =
  { EmailAddress : string
    Status : string }
  
// Outputs
type PostCreatedEvent = PostCreatedEvent of Post
type CommenterValidatedEvent = CommenterValidatedEvent of Commenter
type CommentPostedEvent = CommentPostedEvent of Post * Comment
type CommentApprovalUpdatedEvent = CommentApprovalUpdatedEvent of Comment
type CommenterStatusUpdatedEvent = CommenterStatusUpdatedEvent of Commenter

// Actions

type IPostActions =
  abstract member CreatePost: newPost: NewPostDto -> CancellableTask<ActionResult<PostCreatedEvent, CreatePostError>>
  abstract member ValidateCommenter: commenterValidation: CommenterValidationDto -> CancellableTask<ActionResult<CommenterValidatedEvent, ValidateCommenterError>>
  abstract member PostComment: newComment: NewCommentDto -> CancellableTask<ActionResult<CommentPostedEvent, PostCommentError>>
  abstract member UpdateCommentApproval: CommentApprovalUpdateDto -> CancellableTask<ActionResult<CommentApprovalUpdatedEvent, UpdateCommentApprovalError>>
  abstract member UpdateCommenterStatus: CommenterStatusUpdateDto -> CancellableTask<ActionResult<CommenterStatusUpdatedEvent, UpdateCommenterStatusError>>

[<RequireQualifiedAccess>]
module PostActions =
  
  open FsToolkit.ErrorHandling
  
  let withDependencies (postDependencies: IPostDependencies) =
    
    let handleCommenterValidated (CommenterValidated (commenter, commenterValidation)) : DependencyResult<Commenter> =
      cancellableTaskResult {
        let! (commenter: Commenter) = cancellableTaskResult {
          match commenter with
          | Some c -> return c
          | None -> return! postDependencies.CreateCommenter commenterValidation.EmailAddress commenterValidation.Name CommenterStatus.New
        }
        
        do!
          postDependencies.SendCommenterValidationEmail commenter commenterValidation.RedirectUri
          |> CancellableTaskResult.ignore
      
        return commenter
      }
    
    let handleCommentPosted (CommentPosted (post, comment, commenter)) : DependencyResult<Comment> =
      postDependencies.CreateComment post commenter comment
      
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
    
    { new IPostActions with
    
      member this.CreatePost newPost = cancellableTaskResult {
        let! (newPost: NewPost) =
          NewPost.create
            newPost.Title
            newPost.Slug
          |> Result.mapError ActionError<CreatePostError>.Validation
                      
        let! existingPost =
          postDependencies.GetPostBySlug newPost.Slug
          |> CancellableTaskResult.mapError ActionError<CreatePostError>.Dependency
          
        let decision = Posts.createPost existingPost newPost
          
        return!
          decision
          |> Result.map (fun (PostCreated newPost) -> newPost)
          |> Result.traverseCancellableTask postDependencies.CreatePost
          |> CancellableTaskResult.foldResult
            (DependencyResult.toActionResult PostCreatedEvent)
            (ActionError.Logic >> ActionResult.failure)
      }
      
      member this.ValidateCommenter commenterValidation = cancellableTaskResult {
        let! (commenterValidation: CommenterValidation) =
          CommenterValidation.create
            commenterValidation.EmailAddress
            commenterValidation.Name
            commenterValidation.RedirectUri
          |> Result.mapError ActionError<ValidateCommenterError>.Validation
            
        let! existingCommenter =
          postDependencies.GetCommenterByEmailAddress commenterValidation.EmailAddress
          |> CancellableTaskResult.mapError ActionError<ValidateCommenterError>.Dependency
          
        let! validRedirectUris =
          postDependencies.GetValidRedirectUris()
          |> CancellableTaskResult.mapError ActionError<ValidateCommenterError>.Dependency
            
        let decision = Posts.validateCommenter existingCommenter validRedirectUris commenterValidation
                
        return!
          decision
          |> Result.traverseCancellableTask handleCommenterValidated
          |> CancellableTaskResult.foldResult
            (DependencyResult.toActionResult CommenterValidatedEvent)
            (ActionError.Logic >> ActionResult.failure)
      }
        
      member this.PostComment newComment = cancellableTaskResult {
        let! (newComment: NewComment) =
          NewComment.create
            newComment.Post
            newComment.ValidationId
            newComment.Comment
          |> Result.mapError ActionError<PostCommentError>.Validation
            
        let! existingPost =
          postDependencies.GetPostBySlug newComment.Post
          |> CancellableTaskResult.mapError ActionError<PostCommentError>.Dependency
          
        let! existingCommenter =
          postDependencies.GetCommenterByValidationId newComment.ValidationId
          |> CancellableTaskResult.mapError ActionError<PostCommentError>.Dependency
            
        let decision = Posts.postComment existingPost existingCommenter newComment
        
        let commentPostedEvent existingPost comment = CommentPostedEvent (existingPost, comment)
        
        return!
          decision
          |> Result.traverseCancellableTask handleCommentPosted
          |> CancellableTaskResult.foldResult
            (DependencyResult.toActionResult (commentPostedEvent (Option.get existingPost)))
            (ActionError.Logic >> ActionResult.failure)
      }
      
      member this.UpdateCommentApproval approvalUpdate = cancellableTaskResult {
        let! (approvalUpdate: CommentApprovalUpdate) =
          CommentApprovalUpdate.create
            approvalUpdate.CommentId
            approvalUpdate.Approval
          |> Result.mapError ActionError<UpdateCommentApprovalError>.Validation
              
        let! existingComment =
          postDependencies.GetCommentById approvalUpdate.CommentId
          |> CancellableTaskResult.mapError ActionError<UpdateCommentApprovalError>.Dependency
          
        let decision = Posts.updateCommentApproval existingComment approvalUpdate
        
        return!
          decision
          |> Result.traverseCancellableTask handleCommentApprovalUpdated
          |> CancellableTaskResult.foldResult
            (DependencyResult.toActionResult CommentApprovalUpdatedEvent)
            (ActionError.Logic >> ActionResult.failure)
      }
      
      member this.UpdateCommenterStatus statusUpdate = cancellableTaskResult {
        let! (statusUpdate: CommenterStatusUpdate) =
          CommenterStatusUpdate.create
            statusUpdate.EmailAddress
            statusUpdate.Status
          |> Result.mapError ActionError<UpdateCommenterStatusError>.Validation
              
        let! existingCommenter =
          postDependencies.GetCommenterByValidationId statusUpdate.ValidationId
          |> CancellableTaskResult.mapError ActionError<UpdateCommenterStatusError>.Dependency
          
        let decision = Posts.updateCommenterStatus existingCommenter statusUpdate
        
        return!
          decision
          |> Result.traverseCancellableTask handleCommenterStatusUpdated
          |> CancellableTaskResult.foldResult
            (DependencyResult.toActionResult CommenterStatusUpdatedEvent)
            (ActionError.Logic >> ActionResult.failure)
      }
    }
  
  
