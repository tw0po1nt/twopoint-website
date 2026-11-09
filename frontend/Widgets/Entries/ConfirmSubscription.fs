module TwoPoint.Site.Widgets.Entries.ConfirmSubscription

open TwoPoint.Site.Widgets

let private confirmSubscription widget =
  match widget with
  | Contract.Widget.ConfirmSubscription props -> Some props
  | _ -> None

for mount, props in Mounts.find "confirm-subscription" confirmSubscription do
  Mount.replace mount (fun () -> ConfirmSubscriptionView.view props.Api)
