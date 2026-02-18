namespace TwoPoint.Api.Subscriptions

open TwoPoint.Api

type NewSubscription =
  { EmailAddress : string
    Name : string option }

type SubscriptionCancellation =
  { EmailAddress : string }

[<RequireQualifiedAccess>]
module Subscriptions =

  open Fable.Core
  open Fable.Core.JS
  open Fable.SimpleHttp
  open Thoth.Json

  let subscribe uri (newSubscription: NewSubscription) : Promise<ApiResponse<unit>> = promise {
    let body = Encode.Auto.toString newSubscription 

    let! response =
      Http.request $"{uri}/api/blog/subscriptions"
      |> Http.method POST
      |> Http.header (Headers.accept "application/json")
      |> Http.content (BodyContent.Text body)
      |> Http.send
      |> Async.StartAsPromise
    
    return
      Decode.Auto.fromString<ApiResponse<unit>>(response.responseText, caseStrategy = CamelCase)
      |> Result.defaultValue { Success = false; Message = Some "An invalid response was received"; Data = None }
  }

  let unsubscribe uri (subscriptionCancellation : SubscriptionCancellation) : Promise<ApiResponse<unit>> = promise {
    let body = Encode.Auto.toString subscriptionCancellation

    let! response =
      Http.request $"{uri}/api/blog/subscriptions/cancellations"
      |> Http.method POST
      |> Http.header (Headers.accept "application/json")
      |> Http.content (BodyContent.Text body)
      |> Http.send
      |> Async.StartAsPromise
    
    return
      Decode.Auto.fromString<ApiResponse<unit>>(response.responseText, caseStrategy = CamelCase)
      |> Result.defaultValue { Success = false; Message = Some "An invalid response was received"; Data = None }
  }
