module TwoPoint.Site.Widgets.Entries.Subscribe

open TwoPoint.Site.Widgets

let private subscribe widget =
  match widget with
  | Contract.Widget.Subscribe props -> Some props
  | _ -> None

for mount, props in Mounts.find "subscribe" subscribe do
  Mount.replace mount (fun () -> SubscribeView.view props.Api)
