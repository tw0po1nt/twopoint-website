namespace TwoPoint.Core.Posts.Logic

open FsToolkit.ErrorHandling

open TwoPoint.Core.Posts
open TwoPoint.Core.Shared

open System

// ====================================================================================================================
// Create post
// ====================================================================================================================

// Inputs

type NewPost =
  { Title : NonEmptyString
    Slug : Slug
    CreatedDate : DateTime }

// Outputs

type CreatePostError = PostAlreadyExists of Post

type PostCreated = PostCreated of NewPost

type CreatePostDecision = Decision<PostCreated, CreatePostError>

// Logic

type CreatePost =
  Post option
    -> NewPost
    -> CreatePostDecision

[<RequireQualifiedAccess>]
module CreatePost =

  let decide : CreatePost =
    fun existingPost newPost -> result {
      do! existingPost |> Result.requireNoneWith PostAlreadyExists
      return PostCreated newPost
    } 

// ====================================================================================================================
// Validate commenter
// ====================================================================================================================

// Inputs

type CommenterValidation =
  { EmailAddress : EmailAddress
    Name : NonEmptyString option
    RedirectUri : Uri }
  
// Outputs

type ValidateCommenterError =
  | CommenterBanned of Commenter
  | InvalidRedirect of Uri

type CommenterValidated = CommenterValidated of commenter: Commenter option * validation: CommenterValidation

type ValidateCommenterDecision = Decision<CommenterValidated, ValidateCommenterError>

// Logic

type ValidateCommenter =
  Commenter option
    -> Uri list
    -> CommenterValidation
    -> ValidateCommenterDecision

[<RequireQualifiedAccess>]
module ValidateCommenter =
  
  let decide : ValidateCommenter =
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

// ====================================================================================================================
// Post comment
// ====================================================================================================================

// Inputs

type NewComment =
  { Post : Slug
    ValidationId : CommenterValidationId
    Content : NonEmptyString }

// Outputs

type PostCommentError =
  | PostNotFound of Slug
  | CommenterNotFound of CommenterValidationId
  | CommenterBanned of Commenter

type CommentPosted = CommentPosted of post: Post * comment: NewComment * commenter: Commenter

type PostCommentDecision = Decision<CommentPosted, PostCommentError>

// Logic

type PostComment =
  Post option
    -> Commenter option
    -> NewComment
    -> PostCommentDecision
    
[<RequireQualifiedAccess>]
module PostComment =

  let decide : PostComment =
    fun existingPost existingCommenter newComment -> result {
      let! post = existingPost |> Result.requireSome (PostNotFound newComment.Post)
      let! commenter = existingCommenter |> Result.requireSome (PostCommentError.CommenterNotFound newComment.ValidationId)
      do! commenter.Status.IsBanned |> Result.requireFalse (CommenterBanned existingCommenter.Value)

      return CommentPosted (post, newComment, commenter)
    }
    
// ====================================================================================================================
// Update comment approval
// ====================================================================================================================

// Inputs

type CommentApprovalUpdate =
  { CommentId : CommentId
    Approval : CommentApproval }

// Outputs

type UpdateCommentApprovalError = CommentNotFound of CommentId

type CommentApprovalUpdated = CommentApprovalUpdated of comment: Comment * approval: CommentApproval

type UpdateCommentApprovalDecision = Decision<CommentApprovalUpdated, UpdateCommentApprovalError>

// Logic

type UpdateCommentApproval =
  Comment option
    -> CommentApprovalUpdate
    -> UpdateCommentApprovalDecision
    
[<RequireQualifiedAccess>]
module UpdateCommentApproval =

  let decide : UpdateCommentApproval =
    fun existingComment approvalUpdate -> result {
      let! comment = existingComment |> Result.requireSome (UpdateCommentApprovalError.CommentNotFound approvalUpdate.CommentId)
      return CommentApprovalUpdated (comment, approvalUpdate.Approval)
    }

// ====================================================================================================================
// Delete comment
// ====================================================================================================================

// Inputs

type CommentDeletion = { CommentId : CommentId }

// Outputs

type DeleteCommentError = CommentNotFound of CommentId

type CommentDeleted = CommentDeleted of comment: Comment

type DeleteCommentDecision = Decision<CommentDeleted, DeleteCommentError>

// Logic

type DeleteComment =
  Comment option
    -> CommentDeletion
    -> DeleteCommentDecision
 
module DeleteComment =

  let decide : DeleteComment =
    fun existingComment deletion -> result {
      let! comment = existingComment |> Result.requireSome (DeleteCommentError.CommentNotFound deletion.CommentId)
      return CommentDeleted comment
    }  

// ====================================================================================================================
// Update commenter status
// ====================================================================================================================

// Inputs

type CommenterStatusUpdate =
  { ValidationId : CommenterValidationId
    Status : CommenterStatus }

// Outputs

type UpdateCommenterStatusError = CommenterNotFound of CommenterValidationId

type CommenterStatusUpdated = CommenterStatusUpdated of commenter: Commenter * status: CommenterStatus

type UpdateCommenterStatusDecision = Decision<CommenterStatusUpdated, UpdateCommenterStatusError>

// Logic

type UpdateCommenterStatus =
  Commenter option
    -> CommenterStatusUpdate
    -> UpdateCommenterStatusDecision
  
[<RequireQualifiedAccess>]
module UpdateCommenterStatus =

  let decide : UpdateCommenterStatus =
    fun existingCommenter statusUpdate -> result {
      let! commenter = existingCommenter |> Result.requireSome (CommenterNotFound statusUpdate.ValidationId)
      return CommenterStatusUpdated (commenter, statusUpdate.Status)
    }
