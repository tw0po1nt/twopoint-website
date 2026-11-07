[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.StopReplyEmailsView

open TwoPoint.Api.ReplyEmails

open Browser
open Fable.Core
open Oxpecker.Solid
open Oxpecker.Solid.Aria

let private perform (api: string) (effect: StopReplyEmails.Effect) (dispatch: StopReplyEmails.Msg -> unit) =
  match effect with
  | StopReplyEmails.Read token ->
    ReplyEmails.read api token
    |> Promise.map (StopReplyEmails.StateRead >> dispatch)
    |> Promise.catch (fun _ -> dispatch StopReplyEmails.StateUnread)
    |> Promise.start
  | StopReplyEmails.Stop token ->
    ReplyEmails.stop api token
    |> Promise.map (StopReplyEmails.StopAnswered >> dispatch)
    |> Promise.catch (fun _ -> dispatch StopReplyEmails.StopUnanswered)
    |> Promise.start

[<Emit "[...new URLSearchParams($0)]">]
let private queryPairs (search: string) : (string * string) array = jsNative

let private statusClass (model: StopReplyEmails.Model) =
  match model with
  | StopReplyEmails.Stopped -> "stop-reply-emails-status is-done"
  | StopReplyEmails.MissingToken
  | StopReplyEmails.Unknown
  | StopReplyEmails.Unreadable _ -> "stop-reply-emails-status is-failed"
  | StopReplyEmails.Reading _
  | StopReplyEmails.Active _
  | StopReplyEmails.Stopping _
  | StopReplyEmails.StopFailed _ -> "stop-reply-emails-status"

let private statusText (model: StopReplyEmails.Model) =
  match model with
  | StopReplyEmails.Reading _ -> "Loading…"
  | StopReplyEmails.MissingToken
  | StopReplyEmails.Unknown -> "This link doesn't work. Use the stop link from a reply email."
  | StopReplyEmails.Unreadable StopReplyEmails.Refused -> "Couldn't load this link. Try again later."
  | StopReplyEmails.Unreadable StopReplyEmails.Unreachable -> "Couldn't reach the server. Try again later."
  | StopReplyEmails.Active(_, postTitle)
  | StopReplyEmails.Stopping(_, postTitle)
  | StopReplyEmails.StopFailed(_, postTitle, _) -> $"Stop reply emails for your comment on {postTitle}?"
  | StopReplyEmails.Stopped -> "You get no more reply emails for this comment."

let private asks (model: StopReplyEmails.Model) =
  match model with
  | StopReplyEmails.Active _
  | StopReplyEmails.Stopping _
  | StopReplyEmails.StopFailed _ -> true
  | StopReplyEmails.MissingToken
  | StopReplyEmails.Reading _
  | StopReplyEmails.Unreadable _
  | StopReplyEmails.Stopped
  | StopReplyEmails.Unknown -> false

let private isStopping (model: StopReplyEmails.Model) =
  match model with
  | StopReplyEmails.Stopping _ -> true
  | _ -> false

let private errorText (model: StopReplyEmails.Model) =
  match model with
  | StopReplyEmails.StopFailed(_, _, StopReplyEmails.Refused) -> "Couldn't stop the reply emails. Try again."
  | StopReplyEmails.StopFailed(_, _, StopReplyEmails.Unreachable) -> "Couldn't reach the server. Try again."
  | _ -> ""

[<SolidComponent>]
let view (api: string) =
  let model, dispatch =
    Mvu.run (StopReplyEmails.init (List.ofArray (queryPairs window.location.search))) StopReplyEmails.update (perform api)

  div (class' = "stop-reply-emails") {
    p(class' = statusClass (model ())).attr ("aria-live", "polite") { statusText (model ()) }

    Show(when' = asks (model ())) {
      button (type' = "button", disabled = isStopping (model ()), onClick = fun _ -> dispatch StopReplyEmails.StopSelected) {
        if isStopping (model ()) then "[sending]" else "[stop reply emails]"
      }
    }

    Show(when' = (errorText (model ()) <> "")) {
      p (class' = "stop-reply-emails-error", role = "alert") { errorText (model ()) }
    }
  }
