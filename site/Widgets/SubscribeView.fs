[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.SubscribeView

open TwoPoint.Api.Subscriptions

open Browser.Types
open Oxpecker.Solid
open Oxpecker.Solid.Aria

let private perform (api: string) (effect: Subscribe.Effect) (dispatch: Subscribe.Msg -> unit) =
  match effect with
  | Subscribe.Send subscription ->
    Subscriptions.subscribe api subscription
    |> Promise.map (Subscribe.Answered >> dispatch)
    |> Promise.catch (fun _ -> dispatch Subscribe.Unanswered)
    |> Promise.start

let private text (event: Event) = (event.target :?> HTMLInputElement).value

let private buttonText (model: Subscribe.Model) =
  if Subscribe.isSending model then "[sending]" else "[enter]"

let private statusClass (model: Subscribe.Model) =
  match model.Status with
  | Subscribe.Failed _ -> "subscribe-status is-failed"
  | Subscribe.Subscribed -> "subscribe-status is-subscribed"
  | Subscribe.Editing
  | Subscribe.Sending -> "subscribe-status"

let private statusText (model: Subscribe.Model) =
  match model.Status with
  | Subscribe.Failed reason -> reason
  | Subscribe.Subscribed -> "You are subscribed. The next post comes to your inbox."
  | Subscribe.Editing
  | Subscribe.Sending -> ""

// JSX drops the space at the end of a text line and refuses a `>` in text, so these strings go in as values.
let private emailLabel = "email>"
let private nameLabel = "name>"
let private notePrefix = "The site uses your email address only to send the newsletter. See the "

[<SolidComponent>]
let view (api: string) =
  let model, dispatch = Mvu.run (Subscribe.init, None) Subscribe.update (perform api)

  form (
    class' = "subscribe",
    novalidate = true,
    onSubmit =
      fun event ->
        event.preventDefault ()
        dispatch Subscribe.Submitted
  ) {
    label (class' = "subscribe-field") {
      span () { emailLabel }

      input (
        type' = "email",
        name = "email",
        autocomplete = "email",
        placeholder = "you@example.com",
        value = model().Email,
        onInput = (fun event -> dispatch (Subscribe.EmailChanged(text event))),
        onBlur = fun _ -> dispatch Subscribe.EmailLeft
      )
    }

    Show(when' = Subscribe.showsEmailError (model ())) {
      p (class' = "subscribe-error", role = "alert") { "Give a valid email address." }
    }

    label (class' = "subscribe-field") {
      span () { nameLabel }

      input (
        type' = "text",
        name = "name",
        autocomplete = "name",
        placeholder = "optional",
        maxlength = 256,
        value = model().Name,
        onInput = fun event -> dispatch (Subscribe.NameChanged(text event))
      )
    }

    button (type' = "submit", disabled = Subscribe.isSending (model ())) { buttonText (model ()) }
    p(class' = statusClass (model ())).attr ("aria-live", "polite") { statusText (model ()) }

    p (class' = "subscribe-note") {
      notePrefix
      a (href = "/privacy/") { "privacy policy" }
      "."
    }
  }
