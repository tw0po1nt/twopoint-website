namespace TwoPoint.Http.Endpoints

type ApiResponse<'data> =
  { Success : bool
    Message : string option
    Data : 'data option }

/// The Service Bus queue that contains a message for each welcome to send
module WelcomeQueue =
  [<Literal>]
  let Name = "subscription_welcome"
