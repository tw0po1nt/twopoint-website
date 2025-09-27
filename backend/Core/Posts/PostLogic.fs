namespace TwoPoint.Core.Posts.Logic

open FsToolkit.ErrorHandling

open TwoPoint.Core.Posts
open TwoPoint.Core.Shared

open System

// ========================================================================================
// TYPES
// ========================================================================================

// -----------------------------------------------------------------------------------------
// Create post
// -----------------------------------------------------------------------------------------

// Inputs

type NewPost =
  { Title : NonEmptyString
    Slug : Slug }

module NewPost =
  let create title slug = validation {
    let! title = title |> NonEmptyString.create (Some (nameof title))
    let! slug = Slug.create slug
    return { Title = title; Slug = slug }
  }

// Outputs

type CreatePostError = PostAlreadyExists of Post

type PostCreated = PostCreated of NewPost

type CreatePostDecision = Decision<PostCreated, CreatePostError>

/// <summary>
/// Logic for creating a TwoPoint blog post
/// </summary>
type CreatePost =
  Post option
    -> NewPost
    -> CreatePostDecision
    
// ----------------------------------------------------------------------------------------
// Validate commenter
// ----------------------------------------------------------------------------------------

// Inputs
type CommenterValidation =
  { EmailAddress : EmailAddress
    Name : NonEmptyString option
    RedirectUri : Uri }
  
module CommenterValidation =
  let create emailAddress name redirectUri = validation {
    let! email = EmailAddress.create emailAddress
    let! name = NonEmptyString.createMaybe (Some (nameof name)) name
    let! redirectUri = Uri.TryValidate(redirectUri, nameof redirectUri)
    return { EmailAddress = email; Name = name; RedirectUri = redirectUri }
  }
  
// Outputs
type ValidateCommenterError =
  | CommenterBanned of Commenter
  | InvalidRedirect of Uri

type CommenterValidated = CommenterValidated of commenter: Commenter option * validation: CommenterValidation

type ValidateCommenterDecision = Decision<CommenterValidated, ValidateCommenterError>

/// <summary>
/// Logic for validating a TwoPoint blog commenter
/// </summary>
type ValidateCommenter =
  Commenter option
    -> Uri list
    -> CommenterValidation
    -> ValidateCommenterDecision

// ----------------------------------------------------------------------------------------
// Post comment
// ----------------------------------------------------------------------------------------

// Inputs
type NewComment =
  { Post : Slug
    ValidationId : CommenterValidationId
    Content : NonEmptyString }
  
module NewComment =
  let create post validationId content = validation {
    let! post = Slug.create post
    let! validationId = CommenterValidationId.create validationId
    let! content = NonEmptyString.create (Some (nameof content)) content
    return { Post = post; ValidationId = validationId; Content = content }
  }
  
// Outputs
type PostCommentError =
  | PostNotFound of Slug
  | CommenterNotFound of CommenterValidationId
  | CommenterBanned of Commenter

type CommentPosted = CommentPosted of post: Post * comment: NewComment * commenter: Commenter

type PostCommentDecision = Decision<CommentPosted, PostCommentError>

/// <summary>
/// Logic for commenting on a TwoPoint blog post
/// </summary>
type PostComment =
  Post option
    -> Commenter option
    -> NewComment
    -> PostCommentDecision
    
// ----------------------------------------------------------------------------------------
// Update comment approval
// ----------------------------------------------------------------------------------------

// Inputs
type CommentApprovalUpdate =
  { CommentId : CommentId
    Approval : CommentApproval }
  
module CommentApprovalUpdate =
  let create commentId approval = validation {
    let! commentId = CommentId.create commentId
    let! approval = CommentApproval.create approval
    return { CommentId = commentId; Approval = approval }
  }
  
// Outputs
type UpdateCommentApprovalError = CommentNotFound of CommentId

type CommentApprovalUpdated = CommentApprovalUpdated of comment: Comment * approval: CommentApproval

type UpdateCommentApprovalDecision = Decision<CommentApprovalUpdated, UpdateCommentApprovalError>

/// <summary>
/// Logic for updating the approval status on a TwoPoint blog post comment
/// </summary>
type UpdateCommentApproval =
  Comment option
    -> CommentApprovalUpdate
    -> UpdateCommentApprovalDecision
  
// ----------------------------------------------------------------------------------------
// Update commenter status
// ----------------------------------------------------------------------------------------

// Inputs
type CommenterStatusUpdate =
  { ValidationId : CommenterValidationId
    Status : CommenterStatus }
  
module CommenterStatusUpdate =
  let create validationId status = validation {
    let! validationId = CommenterValidationId.create validationId
    let! status = CommenterStatus.create status
    return { ValidationId = validationId; Status = status }
  }
  
// Outputs
type UpdateCommenterStatusError = CommenterNotFound of CommenterValidationId

type CommenterStatusUpdated = CommenterStatusUpdated of commenter: Commenter * status: CommenterStatus

type UpdateCommenterStatusDecision = Decision<CommenterStatusUpdated, UpdateCommenterStatusError>

/// <summary>
/// Logic for updating the status of a TwoPoint blog commenter
/// </summary>
type UpdateCommenterStatus =
  Commenter option
    -> CommenterStatusUpdate
    -> UpdateCommenterStatusDecision
  
[<RequireQualifiedAccess>]
module Posts =
  
  let createPost : CreatePost =
    fun existingPost newPost -> result {
      do! existingPost |> Result.requireNoneWith PostAlreadyExists
      return PostCreated newPost
    }
    
  let validateCommenter : ValidateCommenter =
    fun existingCommenter validRedirectUris commenterValidation -> result {
      do! result {
        match existingCommenter with
        | Some commenter ->
          return! commenter.Status.IsBanned |> Result.requireFalse (ValidateCommenterError.CommenterBanned existingCommenter.Value)
        | None ->
          return ()
      }
      
      do!
        validRedirectUris
        |> List.tryFind (fun uri -> uri.Host = commenterValidation.RedirectUri.Host)
        |> Result.requireSome (InvalidRedirect commenterValidation.RedirectUri)
        |> Result.ignore
      
      return CommenterValidated (existingCommenter, commenterValidation)
    }
  
  let postComment : PostComment =
    fun existingPost existingCommenter newComment -> result {
      let! post = existingPost |> Result.requireSome (PostNotFound newComment.Post)
      let! commenter = existingCommenter |> Result.requireSome (PostCommentError.CommenterNotFound newComment.ValidationId)
      do! commenter.Status.IsBanned |> Result.requireFalse (CommenterBanned existingCommenter.Value)
      
      return CommentPosted (post, newComment, commenter)
    }
    
  let updateCommentApproval : UpdateCommentApproval =
    fun existingComment approvalUpdate -> result {
      let! comment = existingComment |> Result.requireSome (CommentNotFound approvalUpdate.CommentId)
      return CommentApprovalUpdated (comment, approvalUpdate.Approval)
    }
    
  let updateCommenterStatus : UpdateCommenterStatus =
    fun existingCommenter statusUpdate -> result {
      let! commenter = existingCommenter |> Result.requireSome (CommenterNotFound statusUpdate.ValidationId)
      return CommenterStatusUpdated (commenter, statusUpdate.Status)
    }
