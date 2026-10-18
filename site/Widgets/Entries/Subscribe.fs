module TwoPoint.Site.Widgets.Entries.Subscribe

open TwoPoint.Site.Widgets

Mount.each "subscribe" (fun (Contract.Widget.Subscribe props) -> SubscribeView.view props.Api)
