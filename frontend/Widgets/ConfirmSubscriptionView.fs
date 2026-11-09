[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.ConfirmSubscriptionView

open TwoPoint.Api.Subscriptions

open Browser
open Fable.Core
open Oxpecker.Solid
open Oxpecker.Solid.Aria

let private perform (api: string) (effect: ConfirmSubscription.Effect) (dispatch: ConfirmSubscription.Msg -> unit) =
  match effect with
  | ConfirmSubscription.Confirm subscriptionId ->
    Subscriptions.confirm api subscriptionId
    |> Promise.map (ConfirmSubscription.Answered >> dispatch)
    |> Promise.catch (fun _ -> dispatch ConfirmSubscription.Unanswered)
    |> Promise.start

[<Emit "[...new URLSearchParams($0)]">]
let private queryPairs (search: string) : (string * string) array = jsNative

let private statusClass (model: ConfirmSubscription.Model) =
  match model with
  | ConfirmSubscription.Confirmed -> "confirm-subscription-status is-done"
  | ConfirmSubscription.MissingId
  | ConfirmSubscription.Unknown -> "confirm-subscription-status is-failed"
  | ConfirmSubscription.Ready _
  | ConfirmSubscription.Confirming _
  | ConfirmSubscription.ConfirmFailed _ -> "confirm-subscription-status"

let private statusText (model: ConfirmSubscription.Model) =
  match model with
  | ConfirmSubscription.MissingId -> "This link is missing a subscription ID. Use the link in your confirmation email."
  | ConfirmSubscription.Unknown -> "This link doesn't work."
  | ConfirmSubscription.Ready _
  | ConfirmSubscription.Confirming _
  | ConfirmSubscription.ConfirmFailed _ -> "Confirm your subscription to get new posts by email."
  | ConfirmSubscription.Confirmed -> "You're subscribed! The next post will land in your inbox."

let private asks (model: ConfirmSubscription.Model) =
  match model with
  | ConfirmSubscription.Ready _
  | ConfirmSubscription.Confirming _
  | ConfirmSubscription.ConfirmFailed _ -> true
  | ConfirmSubscription.MissingId
  | ConfirmSubscription.Confirmed
  | ConfirmSubscription.Unknown -> false

let private isConfirming (model: ConfirmSubscription.Model) =
  match model with
  | ConfirmSubscription.Confirming _ -> true
  | ConfirmSubscription.MissingId
  | ConfirmSubscription.Ready _
  | ConfirmSubscription.ConfirmFailed _
  | ConfirmSubscription.Confirmed
  | ConfirmSubscription.Unknown -> false

let private errorText (model: ConfirmSubscription.Model) =
  match model with
  | ConfirmSubscription.ConfirmFailed(_, ConfirmSubscription.Refused) -> Some "Couldn't confirm your subscription. Try again."
  | ConfirmSubscription.ConfirmFailed(_, ConfirmSubscription.Unreachable) -> Some "Couldn't reach the server. Try again."
  | ConfirmSubscription.MissingId
  | ConfirmSubscription.Ready _
  | ConfirmSubscription.Confirming _
  | ConfirmSubscription.Confirmed
  | ConfirmSubscription.Unknown -> None

// JSX drops the space at the end of a text line, so this string goes in as a value.
let private againPrefix = "You can subscribe again on the "

[<SolidComponent>]
let view (api: string) =
  let model, dispatch =
    Mvu.run (ConfirmSubscription.init (List.ofArray (queryPairs window.location.search))) ConfirmSubscription.update (perform api)

  div (class' = "confirm-subscription") {
    p(class' = statusClass (model ())).attr ("aria-live", "polite") { statusText (model ()) }

    Show(when' = asks (model ())) {
      button (
        type' = "button",
        disabled = isConfirming (model ()),
        onClick = fun _ -> dispatch ConfirmSubscription.ConfirmSelected
      ) {
        if isConfirming (model ()) then "[sending]" else "[confirm subscription]"
      }
    }

    Show(when' = (errorText (model ())).IsSome) {
      p (class' = "confirm-subscription-error", role = "alert") { errorText (model ()) |> Option.defaultValue "" }
    }

    Show(when' = (model () = ConfirmSubscription.Unknown)) {
      p (class' = "confirm-subscription-note") {
        againPrefix
        a (href = "/subscribe/") { "subscribe page" }
        "."
      }
    }
  }
