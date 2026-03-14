namespace TwoPoint.Core.Posts.Actions

open System
open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Logic
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

// ====================================================================================================================
// Create post
// ====================================================================================================================

// Input

module NewPost =
  
  type Title = Title of string
  type Slug = Slug of string
  type CreatedDate = CreatedDate of string

  type Dto =
    { Title : Title
      Slug : Slug
      CreatedDate : CreatedDate }
    
  let create (dto : Dto) = validation {
    let (Title title) = dto.Title
    let (Slug slug) = dto.Slug
    let (CreatedDate createdDate) = dto.CreatedDate
    let! title = title |> NonEmptyString.create (Some (nameof title))
    and! slug = Slug.create slug
    and! createdDate = DateTime.TryValidateUtc(createdDate, (nameof createdDate))
    
    return
      { NewPost.Title = title
        Slug = slug
        CreatedDate = createdDate }
  }
    
// Output

type PostCreatedEvent = PostCreatedEvent of Post

// Action

type CreatePost =
  NewPost.Dto
    -> CancellableTask<ActionResult<PostCreatedEvent, CreatePostError>>
    
[<RequireQualifiedAccess>]
module CreatePost =
  
  type Dependencies =
    abstract member GetPostBySlug: slug: Slug -> DependencyResult<Post option>
    abstract member CreatePost: newPost: NewPost -> DependencyResult<Post>
    
  let withDependencies (dependencies : Dependencies) : CreatePost =
    fun newPost -> cancellableTaskResult {
      let! newPost =
        newPost
        |> NewPost.create
        |> Result.mapError ActionError<CreatePostError>.Validation
                    
      let! existingPost =
        dependencies.GetPostBySlug newPost.Slug
        |> CancellableTaskResult.mapError ActionError<CreatePostError>.Dependency
        
      let decision = CreatePost.decide existingPost newPost
        
      return!
        decision
        |> Result.map (fun (PostCreated newPost) -> newPost)
        |> Result.traverseCancellableTask dependencies.CreatePost
        |> CancellableTaskResult.foldResult
          (DependencyResult.toActionResult PostCreatedEvent)
          (Logic >> ActionResult.failure) 
    }

// ====================================================================================================================
// Validate commenter
// ====================================================================================================================

// Input

module CommenterValidation =
  
  type EmailAddress = EmailAddress of string
  type Name = Name of string
  type RedirectUri = RedirectUri of string
  
  type Dto =
    { EmailAddress : EmailAddress
      Name : Name option
      RedirectUri : RedirectUri }
    
  let create (dto : Dto) = validation {
    let (EmailAddress emailAddress) = dto.EmailAddress
    let name = dto.Name |> Option.map (fun (Name name) -> name)
    let (RedirectUri redirectUri) = dto.RedirectUri
    let! email = EmailAddress.create emailAddress
    let! name = NonEmptyString.createMaybe (Some (nameof name)) name
    let! redirectUri = Uri.TryValidate(redirectUri, nameof redirectUri)
    return
      { CommenterValidation.EmailAddress = email
        Name = name
        RedirectUri = redirectUri }
  }
  
// Output

type CommenterValidatedEvent = CommenterValidatedEvent of Commenter

// Action

type ValidateCommenter =
  CommenterValidation.Dto
    -> CancellableTask<ActionResult<CommenterValidatedEvent, ValidateCommenterError>>

[<RequireQualifiedAccess>]
module ValidateCommenter =
  
  type Dependencies =
    abstract member GetCommenterByEmailAddress: emailAddress: EmailAddress -> DependencyResult<Commenter option>
    abstract member GetValidRedirectUris: unit -> DependencyResult<Uri list>
    abstract member CreateCommenter:
      emailAddress: EmailAddress
        -> name: NonEmptyString option
        -> status: CommenterStatus
        -> DependencyResult<Commenter>
    abstract member SendCommenterValidationEmail:
      commenter: Commenter
        -> redirectUri: Uri
        -> DependencyResult<OperationId>
  
  let withDependencies (dependencies : Dependencies) : ValidateCommenter =
    let handleCommenterValidated (CommenterValidated (commenter, commenterValidation)) : DependencyResult<Commenter> =
      cancellableTaskResult {
        let! (commenter: Commenter) = cancellableTaskResult {
          match commenter with
          | Some c -> return c
          | None -> return! dependencies.CreateCommenter commenterValidation.EmailAddress commenterValidation.Name CommenterStatus.New
        }
        
        do!
          dependencies.SendCommenterValidationEmail commenter commenterValidation.RedirectUri
          |> CancellableTaskResult.ignore
      
        return commenter
      }
    
    fun commenterValidation -> cancellableTaskResult {
      let! commenterValidation =
        commenterValidation
        |> CommenterValidation.create
        |> Result.mapError ActionError<ValidateCommenterError>.Validation
          
      let! existingCommenter =
        dependencies.GetCommenterByEmailAddress commenterValidation.EmailAddress
        |> CancellableTaskResult.mapError ActionError<ValidateCommenterError>.Dependency
        
      let! validRedirectUris =
        dependencies.GetValidRedirectUris()
        |> CancellableTaskResult.mapError ActionError<ValidateCommenterError>.Dependency
          
      let decision = ValidateCommenter.decide existingCommenter validRedirectUris commenterValidation
              
      return!
        decision
        |> Result.traverseCancellableTask handleCommenterValidated
        |> CancellableTaskResult.foldResult
          (DependencyResult.toActionResult CommenterValidatedEvent)
          (Logic >> ActionResult.failure)
  }
    
// ====================================================================================================================
// Post comment
// ====================================================================================================================

// Input

module NewComment =
  
  type Post = Post of string
  type ValidationId = ValidationId of string
  type Comment = Comment of string
  
  type Dto =
    { Post : Post
      ValidationId : ValidationId
      Comment : Comment }

  let create (dto : Dto) = validation {
    let (Post post) = dto.Post
    let (ValidationId validationId) = dto.ValidationId
    let (Comment content) = dto.Comment
    let! post = Slug.create post
    let! validationId = CommenterValidationId.create validationId
    let! content = NonEmptyString.create (Some (nameof content)) content
    return { Post = post; ValidationId = validationId; Content = content }
  }
  
// Output

type CommentPostedEvent = CommentPostedEvent of Post * Comment

// Action

type PostComment =
  NewComment.Dto
    -> CancellableTask<ActionResult<CommentPostedEvent, PostCommentError>>

[<RequireQualifiedAccess>]
module PostComment =
  
  type Dependencies =
    abstract member GetPostBySlug: slug: Slug -> DependencyResult<Post option>
    abstract member GetCommenterByValidationId: validationId: CommenterValidationId -> DependencyResult<Commenter option>
    abstract member CreateComment:
      post: Post
        -> commenter: Commenter
        -> newComment: NewComment
        -> DependencyResult<Comment>
  
  let withDependencies (dependencies : Dependencies) : PostComment =

    let handleCommentPosted (CommentPosted (post, comment, commenter)) : DependencyResult<Comment> =
      dependencies.CreateComment post commenter comment

    fun newComment -> cancellableTaskResult {
      let! newComment =
        newComment
        |> NewComment.create
        |> Result.mapError ActionError<PostCommentError>.Validation

      let! existingPost =
        dependencies.GetPostBySlug newComment.Post
        |> CancellableTaskResult.mapError ActionError<PostCommentError>.Dependency

      let! existingCommenter =
        dependencies.GetCommenterByValidationId newComment.ValidationId
        |> CancellableTaskResult.mapError ActionError<PostCommentError>.Dependency

      let decision = PostComment.decide existingPost existingCommenter newComment

      let commentPostedEvent existingPost comment = CommentPostedEvent (existingPost, comment)

      return!
        decision
        |> Result.traverseCancellableTask handleCommentPosted
        |> CancellableTaskResult.foldResult
          (DependencyResult.toActionResult (commentPostedEvent (Option.get existingPost)))
          (Logic >> ActionResult.failure) 
    }
  
// ====================================================================================================================
// Update comment approval
// ====================================================================================================================

// Input

module CommentApprovalUpdate =
  
  type CommentId = CommentId of string
  type Approval = Approval of string

  type Dto =
    { CommentId : CommentId 
      Approval : Approval }

  let create (dto : Dto) = validation {
    let (CommentId commentId) = dto.CommentId
    let (Approval approval) = dto.Approval
    let! commentId = CommentId.create commentId
    let! approval = CommentApproval.create approval
    return
      { CommentApprovalUpdate.CommentId = commentId
        Approval = approval }
  }

// Output
  
type CommentApprovalUpdatedEvent = CommentApprovalUpdatedEvent of Comment

// Action

type UpdateCommentApproval =
  CommentApprovalUpdate.Dto
    -> CancellableTask<ActionResult<CommentApprovalUpdatedEvent, UpdateCommentApprovalError>>

[<RequireQualifiedAccess>]
module UpdateCommentApproval =
  
  type Dependencies =
    abstract member GetCommentById:
      commentId: CommentId
        -> DependencyResult<Comment option>
    abstract member UpdateCommenterStatus:
      commenter: Commenter
        -> status: CommenterStatus
        -> DependencyResult<Commenter>
    abstract member UpdateCommentApproval:
      comment: Comment
        -> approval: CommentApproval
        -> DependencyResult<Comment>

  let withDependencies (dependencies : Dependencies) : UpdateCommentApproval =

    let handleCommentApprovalUpdated (CommentApprovalUpdated (comment, approval)) : DependencyResult<Comment> =
      cancellableTaskResult {
        do! cancellableTaskResult {
          match approval with
          | Approved ->
            return!
              CommenterStatus.Trusted
              |> dependencies.UpdateCommenterStatus comment.Commenter
              |> CancellableTaskResult.ignore
          | _ -> return ()
        }

        return!
          dependencies.UpdateCommentApproval comment approval
          |> CancellableTaskResult.map (fun comment ->
            match approval with
            | Approved -> { comment with Comment.Commenter.Status = CommenterStatus.Trusted }
            | _ -> comment
          )
      }
      
    fun approvalUpdate -> cancellableTaskResult {
      let! (approvalUpdate : CommentApprovalUpdate) =
        approvalUpdate
        |> CommentApprovalUpdate.create
        |> Result.mapError ActionError<UpdateCommentApprovalError>.Validation
            
      let! existingComment =
        dependencies.GetCommentById approvalUpdate.CommentId
        |> CancellableTaskResult.mapError ActionError<UpdateCommentApprovalError>.Dependency
        
      let decision = UpdateCommentApproval.decide existingComment approvalUpdate
      
      return!
        decision
        |> Result.traverseCancellableTask handleCommentApprovalUpdated
        |> CancellableTaskResult.foldResult
          (DependencyResult.toActionResult CommentApprovalUpdatedEvent)
          (ActionError.Logic >> ActionResult.failure)
    }

// ====================================================================================================================
// Delete comment
// ====================================================================================================================

// Input

module CommentDeletion =
 
  type CommentId = CommentId of string

  type Dto = { CommentId : CommentId }
  
  let create (dto : Dto) = validation {
    let (CommentId commentId) = dto.CommentId
    let! commentId = CommentId.create commentId
    return { CommentDeletion.CommentId = commentId }
  }

// Output

type CommentDeletedEvent = CommentDeletedEvent of CommentId

// Action

type DeleteComment =
  CommentDeletion.Dto
    -> CancellableTask<ActionResult<CommentDeletedEvent, DeleteCommentError>>
  
[<RequireQualifiedAccess>]
module DeleteComment =

   type Dependencies =
     abstract member GetCommentById: commentId: CommentId -> DependencyResult<Comment option>
     abstract member DeleteComment: comment: Comment -> DependencyResult<unit>

   let withDependencies (dependencies : Dependencies) : DeleteComment =

     let handleCommentDeleted (CommentDeleted comment) : DependencyResult<CommentId> =
       cancellableTaskResult {
         do! dependencies.DeleteComment comment
         return comment.Id
       }
       
     fun commentDeletion -> cancellableTaskResult {
       let! commentDeletion =
         commentDeletion
         |> CommentDeletion.create
         |> Result.mapError ActionError<DeleteCommentError>.Validation

       let! existingComment =
         dependencies.GetCommentById commentDeletion.CommentId
         |> CancellableTaskResult.mapError ActionError<DeleteCommentError>.Dependency

       let decision = DeleteComment.decide existingComment commentDeletion

       return!
         decision
         |> Result.traverseCancellableTask handleCommentDeleted
         |> CancellableTaskResult.foldResult
           (DependencyResult.toActionResult CommentDeletedEvent)
           (Logic >> ActionResult.failure)
     } 

// ====================================================================================================================
// Update commenter status
// ====================================================================================================================

// Input

module CommenterStatusUpdate =

  type ValidationId = ValidationId of string
  type Status = Status of string

  type Dto =
    { ValidationId : ValidationId 
      Status : Status }

  let create (dto : Dto) = validation {
    let (ValidationId validationId) = dto.ValidationId
    let (Status status) = dto.Status
    let! validationId = CommenterValidationId.create validationId
    let! status = CommenterStatus.create status
    return
      { CommenterStatusUpdate.ValidationId = validationId
        Status = status }
  }

// Outputs

type CommenterStatusUpdatedEvent = CommenterStatusUpdatedEvent of Commenter

// Actions

type UpdateCommenterStatus =
  CommenterStatusUpdate.Dto -> CancellableTask<ActionResult<CommenterStatusUpdatedEvent, UpdateCommenterStatusError>>
  
[<RequireQualifiedAccess>]
module UpdateCommenterStatus =
  
  type Dependencies =
    abstract member GetCommenterByValidationId: validationId: CommenterValidationId -> DependencyResult<Commenter option>
    abstract member UpdateCommenterStatus:
      commenter: Commenter
        -> status: CommenterStatus
        -> DependencyResult<Commenter>
  
  let withDependencies (dependencies : Dependencies) : UpdateCommenterStatus =

    let handleCommenterStatusUpdated (CommenterStatusUpdated (commenter, status)) : DependencyResult<Commenter> =
      cancellableTaskResult {
        // todo: call code to reject all comments from commenter if banned
        return! dependencies.UpdateCommenterStatus commenter status
      }

    fun statusUpdate -> cancellableTaskResult {
       let! (statusUpdate: CommenterStatusUpdate) =
         statusUpdate
         |> CommenterStatusUpdate.create
         |> Result.mapError ActionError<UpdateCommenterStatusError>.Validation

       let! existingCommenter =
         dependencies.GetCommenterByValidationId statusUpdate.ValidationId
         |> CancellableTaskResult.mapError ActionError<UpdateCommenterStatusError>.Dependency

       let decision = UpdateCommenterStatus.decide existingCommenter statusUpdate

       return!
         decision
         |> Result.traverseCancellableTask handleCommenterStatusUpdated
         |> CancellableTaskResult.foldResult
           (DependencyResult.toActionResult CommenterStatusUpdatedEvent)
           (Logic >> ActionResult.failure)
    }
