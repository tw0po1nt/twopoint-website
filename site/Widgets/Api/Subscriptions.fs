namespace TwoPoint.Api.Subscriptions

open TwoPoint.Api

type NewSubscription =
  { EmailAddress : string
    Name : string option }

type SubscriptionCancellation =
  { SubscriptionId : string }

[<RequireQualifiedAccess>]
module Subscriptions =

  open Fable.Core
  open Fable.Core.JS
  open Fetch
  open Thoth.Json

  let private post (url: string) (body: string) : Promise<ApiResponse<unit>> = promise {
    let! response =
      fetchUnsafe url [
        Method HttpMethod.POST
        requestHeaders [ Accept "application/json" ]
        Body (U3.Case3 body)
      ]

    let! text = response.text ()

    return
      Decode.Auto.fromString<ApiResponse<unit>>(text, caseStrategy = CamelCase)
      |> Result.defaultValue { Success = false; Message = Some "An invalid response was received"; Data = None }
  }

  let subscribe uri (newSubscription: NewSubscription) : Promise<ApiResponse<unit>> =
    post $"{uri}/api/blog/subscriptions" (Encode.Auto.toString newSubscription)

  let cancel uri (cancellation: SubscriptionCancellation) : Promise<ApiResponse<unit>> =
    post $"{uri}/api/blog/subscriptions/cancellations" (Encode.Auto.toString cancellation)
