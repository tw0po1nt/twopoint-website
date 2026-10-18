namespace TwoPoint.Api.Subscriptions

open TwoPoint.Api

type NewSubscription =
  { EmailAddress : string
    Name : string option }

[<RequireQualifiedAccess>]
module Subscriptions =

  open Fable.Core
  open Fable.Core.JS
  open Fetch
  open Thoth.Json

  let subscribe uri (newSubscription: NewSubscription) : Promise<ApiResponse<unit>> = promise {
    let body = Encode.Auto.toString newSubscription

    let! response =
      fetchUnsafe $"{uri}/api/blog/subscriptions" [
        Method HttpMethod.POST
        requestHeaders [ Accept "application/json" ]
        Body (U3.Case3 body)
      ]

    let! text = response.text ()

    return
      Decode.Auto.fromString<ApiResponse<unit>>(text, caseStrategy = CamelCase)
      |> Result.defaultValue { Success = false; Message = Some "An invalid response was received"; Data = None }
  }
