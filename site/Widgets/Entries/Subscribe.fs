module TwoPoint.Site.Widgets.Entries.Subscribe

open TwoPoint.Site.Widgets

for mount, widget in Mounts.find "subscribe" do
  match widget with
  | Contract.Widget.Subscribe props -> Mount.replace mount (fun () -> SubscribeView.view props.Api)
  | Contract.Widget.Comments _ -> ()
