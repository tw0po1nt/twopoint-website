module TwoPoint.Site.Widgets.Entries.StopReplyEmails

open TwoPoint.Site.Widgets

let private stopReplyEmails widget =
  match widget with
  | Contract.Widget.StopReplyEmails props -> Some props
  | _ -> None

for mount, props in Mounts.find "stop-reply-emails" stopReplyEmails do
  Mount.replace mount (fun () -> StopReplyEmailsView.view props.Api)
