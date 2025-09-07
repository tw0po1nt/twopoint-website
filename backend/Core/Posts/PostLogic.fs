namespace TwoPoint.Core.Posts.Logic

open FsToolkit.ErrorHandling

open TwoPoint.Core.Posts
open TwoPoint.Core.Shared

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
// Post comment
// ----------------------------------------------------------------------------------------

// Inputs
type NewComment =
  { Post : Slug
    EmailAddress : EmailAddress
    Name : NonEmptyString option
    Content : NonEmptyString }
  
module NewComment =
  let create post emailAddress name content = validation {
    let! post = Slug.create post
    let! emailAddress = EmailAddress.create emailAddress
    let! name = NonEmptyString.createMaybe (Some (nameof name)) name
    let! content = NonEmptyString.create (Some (nameof content)) content
    return { Post = post; EmailAddress = emailAddress; Name = name; Content = content }
  }
  
// Outputs
type PostCommentError =
  | PostNotFound of Slug
  | CommenterBanned of Commenter

type CommentPosted = CommentPosted of post: Post * comment: NewComment * commenter: Commenter option

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
  { EmailAddress : EmailAddress
    Status : CommenterStatus }
  
module CommenterStatusUpdate =
  let create emailAddress status = validation {
    let! emailAddress = EmailAddress.create emailAddress
    let! status = CommenterStatus.create status
    return { EmailAddress = emailAddress; Status = status }
  }
  
// Outputs
type UpdateCommenterStatusError = CommenterNotFound of EmailAddress

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
  
  let postComment : PostComment =
    fun existingPost existingCommenter newComment -> result {
      let! post = existingPost |> Result.requireSome (PostNotFound newComment.Post)
      
      do! result {
        match existingCommenter with
        | Some commenter ->
          return! commenter.Status.IsBanned |> Result.requireFalse (CommenterBanned existingCommenter.Value)
        | None ->
          return ()
      }
      
      return CommentPosted (post, newComment, existingCommenter)
    }
    
  let updateCommentApproval : UpdateCommentApproval =
    fun existingComment approvalUpdate -> result {
      let! comment = existingComment |> Result.requireSome (CommentNotFound approvalUpdate.CommentId)
      return CommentApprovalUpdated (comment, approvalUpdate.Approval)
    }
    
  let updateCommenterStatus : UpdateCommenterStatus =
    fun existingCommenter statusUpdate -> result {
      let! commenter = existingCommenter |> Result.requireSome (CommenterNotFound statusUpdate.EmailAddress)
      return CommenterStatusUpdated (commenter, statusUpdate.Status)
    }
