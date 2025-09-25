namespace TwoPoint.Core.Posts

// ====================================================================================================================
// Actions
// ====================================================================================================================

// Inputs

type NewPostDto =
  { Title : string
    Slug : string }
  
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
type CommentPostedEvent = CommentPostedEvent of Post * Comment
type CommentApprovalUpdatedEvent = CommentApprovalUpdatedEvent of Comment
type CommenterStatusUpdatedEvent = CommenterStatusUpdatedEvent of Commenter
