namespace TwoPoint.Api.Subscriptions

open TwoPoint.Api

type NewSubscription =
  { EmailAddress : string
    Name : string option }

type SubscriptionCancellation =
  { SubscriptionId : string }

/// The answer of a confirm. `Unknown` is also the answer for a canceled subscription.
[<RequireQualifiedAccess>]
type ConfirmState =
  | Confirmed
  | Unknown

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

  let private confirmState : Decoder<ConfirmState> =
    Decode.field "state" Decode.string
    |> Decode.andThen (fun state ->
      match state with
      | "confirmed" -> Decode.succeed ConfirmState.Confirmed
      | "unknown" -> Decode.succeed ConfirmState.Unknown
      | other -> Decode.fail $"The state '{other}' is unknown.")

  let private confirmAnswer : Decoder<ApiResponse<ConfirmState>> =
    Decode.object (fun get ->
      { Success = get.Required.Field "success" Decode.bool
        Message = get.Optional.Field "message" Decode.string
        Data = get.Optional.Field "data" confirmState })

  let confirm (api: string) (subscriptionId: string) : Promise<ApiResponse<ConfirmState>> = promise {
    let! response =
      fetchUnsafe
        $"{api}/api/blog/subscriptions/{encodeURIComponent subscriptionId}/confirm"
        [ Method HttpMethod.POST; requestHeaders [ Accept "application/json" ] ]

    let! text = response.text ()

    return
      Decode.fromString confirmAnswer text
      |> Result.defaultValue { Success = false; Message = Some "An invalid response was received"; Data = None }
  }
