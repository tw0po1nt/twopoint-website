namespace TwoPoint.Core.Posts

// =======================================================
// DTOs
// =======================================================

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

// =======================================================
// Events
// =======================================================

type PostEvent =
  | PostCreated of Post
  | CommentPosted of Post * Comment
  | CommentApprovalUpdated of Comment
  | CommenterStatusUpdated of Commenter
