namespace TwoPoint.Http.Endpoints

type ApiResponse<'data> =
  { Success : bool
    Message : string option
    Data : 'data option }

/// The Service Bus queue that contains a message for each welcome to send
module WelcomeQueue =
  [<Literal>]
  let Name = "subscription_welcome"

/// `Post` is the partition of the comment. `Queues-ProcessNewCommentMessage` refuses a message with no `Post`, so it goes to the
/// dead-letter queue.
type NewCommentMessage =
  { Post : string
    CommentId : string }

module NewCommentQueue =
  [<Literal>]
  let Name = "new_comment"

type ReplyEmailMessage =
  { Post : string
    ReplyId : string }

/// The Service Bus queue that contains a message for each first approval of a reply
module ReplyEmailQueue =
  [<Literal>]
  let Name = "reply_email"

type NewPostDeliveryJson =
  { SubscriptionId : string
    Post : string
    Intro : string }

/// The Service Bus queue that contains a message for each new-post delivery to send
module NewPostDeliveryQueue =
  [<Literal>]
  let Name = "new_post_delivery"
