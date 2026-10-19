[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.UnsubscribeView

open TwoPoint.Api.Subscriptions

open Browser
open Fable.Core
open Oxpecker.Solid

let private perform (api: string) (effect: Unsubscribe.Effect) (dispatch: Unsubscribe.Msg -> unit) =
  match effect with
  | Unsubscribe.Cancel cancellation ->
    Subscriptions.cancel api cancellation
    |> Promise.map (Unsubscribe.Answered >> dispatch)
    |> Promise.catch (fun _ -> dispatch Unsubscribe.Unanswered)
    |> Promise.start

[<Emit "[...new URLSearchParams($0)]">]
let private queryPairs (search: string) : (string * string) array = jsNative

let private statusClass (model: Unsubscribe.Model) =
  match model with
  | Unsubscribe.Unsubscribed -> "unsubscribe-status is-done"
  | Unsubscribe.NoSubscriptionId
  | Unsubscribe.Refused _
  | Unsubscribe.Unreachable -> "unsubscribe-status is-failed"
  | Unsubscribe.Sending -> "unsubscribe-status"

let private statusText (model: Unsubscribe.Model) =
  match model with
  | Unsubscribe.Sending -> "[sending] The site sends your unsubscribe request."
  | Unsubscribe.NoSubscriptionId -> "The link has no subscription ID. Use the unsubscribe link in a TwoPoint email."
  | Unsubscribe.Unsubscribed -> "You are unsubscribed. No more posts come to your inbox. Sorry to see you go."
  | Unsubscribe.Refused reason -> reason
  | Unsubscribe.Unreachable -> "The site cannot get to the server. Try again later."

// JSX drops the space at the end of a text line, so this string goes in as a value.
let private againPrefix = "If you unsubscribed by mistake, you can subscribe again on the "

[<SolidComponent>]
let view (api: string) =
  let model, _ =
    Mvu.run (Unsubscribe.init (List.ofArray (queryPairs window.location.search))) Unsubscribe.update (perform api)

  div (class' = "unsubscribe") {
    p(class' = statusClass (model ())).attr ("aria-live", "polite") { statusText (model ()) }

    Show(when' = (model () = Unsubscribe.Unsubscribed)) {
      p (class' = "unsubscribe-note") {
        againPrefix
        a (href = "/subscribe/") { "subscribe page" }
        "."
      }
    }
  }
