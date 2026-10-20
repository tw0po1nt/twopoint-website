module TwoPoint.Site.Widgets.Entries.Unsubscribe

open TwoPoint.Site.Widgets

let private unsubscribe widget =
  match widget with
  | Contract.Widget.Unsubscribe props -> Some props
  | _ -> None

for mount, props in Mounts.find "unsubscribe" unsubscribe do
  Mount.replace mount (fun () -> UnsubscribeView.view props.Api)
