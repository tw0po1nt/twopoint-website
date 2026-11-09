module TwoPoint.Site.Widgets.Tests.ConfirmSubscriptionTests

open TwoPoint.Api
open TwoPoint.Api.Subscriptions
open TwoPoint.Site.Widgets

open Expecto

let private id = "2f1c4ed9-6a5b-4f0e-9a43-3d2b8c1e7a10"

let private answer (state: ConfirmState) : ApiResponse<ConfirmState> =
  { Success = true
    Message = None
    Data = Some state }

let private refusal : ApiResponse<ConfirmState> =
  { Success = false
    Message = Some "The table is down"
    Data = None }

let private confirming = ConfirmSubscription.Confirming id

[<Tests>]
let tests =
  testList "ConfirmSubscription" [
    test "a link with a subscription ID waits for the button and sends nothing" {
      Expect.equal
        (ConfirmSubscription.init [ "utm_source", "email"; "subscriptionId", $" {id} " ])
        (ConfirmSubscription.Ready id, None)
        "Ready, with no effect"
    }

    test "a link with no subscription ID or an empty one has no ID" {
      for query in [ []; [ "subscriptionId", "  " ]; [ "token", id ] ] do
        Expect.equal (ConfirmSubscription.init query) (ConfirmSubscription.MissingId, None) $"%A{query} gives no ID"
    }

    test "the button sends one confirm" {
      Expect.equal
        (ConfirmSubscription.update ConfirmSubscription.ConfirmSelected (ConfirmSubscription.Ready id))
        (confirming, Some(ConfirmSubscription.Confirm id))
        "One confirm"

      Expect.equal
        (ConfirmSubscription.update ConfirmSubscription.ConfirmSelected confirming)
        (confirming, None)
        "No second confirm while one is sent"
    }

    test "each answer of the confirm gives its state" {
      Expect.equal
        (ConfirmSubscription.update (ConfirmSubscription.Answered(answer ConfirmState.Confirmed)) confirming)
        (ConfirmSubscription.Confirmed, None)
        "Confirmed"

      Expect.equal
        (ConfirmSubscription.update (ConfirmSubscription.Answered(answer ConfirmState.Unknown)) confirming)
        (ConfirmSubscription.Unknown, None)
        "Unknown"
    }

    test "a refusal or no answer keeps the ID so the button can send again" {
      Expect.equal
        (ConfirmSubscription.update (ConfirmSubscription.Answered refusal) confirming)
        (ConfirmSubscription.ConfirmFailed(id, ConfirmSubscription.Refused), None)
        "Refused"

      let unreachable = ConfirmSubscription.ConfirmFailed(id, ConfirmSubscription.Unreachable)

      Expect.equal
        (ConfirmSubscription.update ConfirmSubscription.Unanswered confirming)
        (unreachable, None)
        "Unreachable"

      Expect.equal
        (ConfirmSubscription.update ConfirmSubscription.ConfirmSelected unreachable)
        (confirming, Some(ConfirmSubscription.Confirm id))
        "A second try"
    }

    test "a missing ID ignores the button" {
      Expect.equal
        (ConfirmSubscription.update ConfirmSubscription.ConfirmSelected ConfirmSubscription.MissingId)
        (ConfirmSubscription.MissingId, None)
        "No confirm"
    }
  ]
