namespace TwoPoint.Core.Posts

open TwoPoint.Core.Shared

open System

// ====================================================================================================================
// Actions
// ====================================================================================================================

// Inputs

type NewPostDto =
  { Title : string
    Slug : string }
  
type NewCommentDto =
  { Post : string
    EmailAddress : string
    Name : string option
    Comment : string }
  
type CommentApprovalUpdateDto =
  { CommentId : string
    Approval : string }
  
type CommenterStatusUpdateDto =
  { EmailAddress : string
    Status : string }
  
// Outputs
type PostCreatedEvent = PostCreatedEvent of Post
type CommentPostedEvent = CommentPostedEvent of Post * Comment
type CommentApprovalUpdatedEvent = CommentApprovalUpdatedEvent of Comment
type CommenterStatusUpdatedEvent = CommenterStatusUpdatedEvent of Commenter
  
// ====================================================================================================================
// Queries
// ====================================================================================================================

