module TwoPoint.Core.Tests.CreateSubscriptionTests

open TwoPoint.Core.Subscriptions.Actions
open TwoPoint.Core.Tests.Fixtures

open Expecto

let private newSubscription : NewSubscription =
  { EmailAddress = (subscription true).EmailAddress
    Name = None }

[<Tests>]
let tests =
  testList "CreateSubscription.Logic.decide" [
    test "a new email address creates a subscription" {
      let decision = CreateSubscription.Logic.decide None newSubscription
      Expect.equal decision (Ok (CreateSubscription.New newSubscription)) "Expected a new subscription"
    }

    test "an inactive subscription is reactivated" {
      let existing = subscription false
      let decision = CreateSubscription.Logic.decide (Some existing) newSubscription
      Expect.equal decision (Ok (CreateSubscription.Reactivate existing)) "Expected a reactivation"
    }

    test "an active subscription is already active" {
      let existing = subscription true
      let decision = CreateSubscription.Logic.decide (Some existing) newSubscription
      Expect.equal decision (Error (CreateSubscription.AlreadyActive existing)) "Expected no change"
    }
  ]
