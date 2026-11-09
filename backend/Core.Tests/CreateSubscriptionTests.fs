module TwoPoint.Core.Tests.CreateSubscriptionTests

open TwoPoint.Core.Subscriptions
open TwoPoint.Core.Subscriptions.Actions
open TwoPoint.Core.Tests.Fixtures
open TwoPoint.Core.Util

open Expecto
open FsToolkit.ErrorHandling

open System
open System.Threading

let private utcNow = DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc)

let private newSubscription : NewSubscription =
  { EmailAddress = (subscription Active).EmailAddress
    Name = None }

let private decide existing = CreateSubscription.Logic.decide utcNow existing newSubscription

let private newId = SubscriptionId.create "5b9e2c4a-7d1f-4a3b-8e6c-0f2d4a6b8c1e" |> ok

type private FakeDependencies(existing : Subscription option, sendFails : bool) =
  let log = ResizeArray<string>()

  member _.Log = List.ofSeq log

  interface CreateSubscription.Dependencies with
    member _.GetSubscriptionByEmail _ = CancellableTaskResult.singleton existing
    member _.GetUtcNow () = utcNow
    member _.NewSubscriptionId () = newId
    member _.SendConfirmation subscription =
      log.Add $"send {subscription.Id} {subscription.Status}"
      if sendFails then CancellableTaskResult.error { Dependency = Dependency.Communications; Type = Unknown (exn "down") }
      else CancellableTaskResult.singleton ()
    member _.CreateSubscription subscription =
      log.Add $"create {subscription.Id} {subscription.Status}"
      CancellableTaskResult.singleton subscription
    member _.UpdateSubscriptionStatus subscription =
      log.Add $"update {subscription.Id} {subscription.Status}"
      CancellableTaskResult.singleton subscription

let private subscribe (dependencies : FakeDependencies) =
  let dto =
    { NewSubscription.Dto.EmailAddress = NewSubscription.EmailAddress "reader@example.com"
      NewSubscription.Dto.Name = Some (NewSubscription.Name "Reader") }
  (CreateSubscription.withDependencies dependencies dto CancellationToken.None).Result

let private decideTests =
  testList "CreateSubscription.Logic.decide" [
    test "a new email address creates a subscription" {
      Expect.equal (decide None) (Ok (CreateSubscription.New newSubscription)) "Expected a new subscription"
    }

    test "a canceled subscription asks for a confirmation again" {
      let existing = subscription canceledStatus
      Expect.equal (decide (Some existing)) (Ok (CreateSubscription.Request existing)) "Expected a request"
    }

    test "an active subscription is already active" {
      let existing = subscription Active
      Expect.equal (decide (Some existing)) (Error (CreateSubscription.AlreadyActive existing)) "Expected no change"
    }

    test "a pending subscription with a request in the last hour sends no second confirmation" {
      let existing = subscription (SubscriptionStatus.Pending (utcNow.AddMinutes(-59.0)))
      Expect.equal (decide (Some existing)) (Error (CreateSubscription.ConfirmationRecent existing)) "Expected no send"
    }

    test "a pending subscription with a request an hour ago or more gets a new confirmation" {
      for requested in [ utcNow.AddHours(-1.0); utcNow.AddDays(-2.0) ] do
        let existing = subscription (SubscriptionStatus.Pending requested)
        Expect.equal (decide (Some existing)) (Ok (CreateSubscription.Request existing)) $"Expected a request at {requested}"
    }
  ]

let private actionTests =
  testList "CreateSubscription action" [
    test "a new subscription is pending, and its row goes after the confirmation" {
      let dependencies = FakeDependencies (None, false)

      match subscribe dependencies with
      | Ok (CreateSubscription.Event.Created created) ->
        Expect.equal created.Id newId "Expected the new ID"
        Expect.equal created.Status (SubscriptionStatus.Pending utcNow) "Expected a pending subscription"
        Expect.equal created.CreatedDate utcNow "Expected the date of the request"
        Expect.equal
          dependencies.Log
          [ $"send {newId} {SubscriptionStatus.Pending utcNow}"; $"create {newId} {SubscriptionStatus.Pending utcNow}" ]
          "Expected the send before the row"
      | other -> failtestf "Expected a new subscription, got %A" other
    }

    test "a failed confirmation writes no row" {
      let dependencies = FakeDependencies (None, true)

      match subscribe dependencies with
      | Error actionError ->
        Expect.isTrue actionError.IsDependency "Expected a dependency error"
        Expect.equal dependencies.Log [ $"send {newId} {SubscriptionStatus.Pending utcNow}" ] "Expected no row"
      | other -> failtestf "Expected an error, got %A" other
    }

    test "a canceled subscription becomes pending with the date of the request, after the confirmation" {
      let existing = subscription canceledStatus
      let dependencies = FakeDependencies (Some existing, false)

      match subscribe dependencies with
      | Ok (CreateSubscription.Event.Requested requested) ->
        Expect.equal requested { existing with Status = SubscriptionStatus.Pending utcNow } "Expected a pending subscription"
        Expect.equal
          dependencies.Log
          [ $"send {existing.Id} {SubscriptionStatus.Pending utcNow}"; $"update {existing.Id} {SubscriptionStatus.Pending utcNow}" ]
          "Expected the send before the update"
      | other -> failtestf "Expected a request, got %A" other
    }

    test "an active subscription sends nothing and writes nothing" {
      let dependencies = FakeDependencies (Some (subscription Active), false)

      match subscribe dependencies with
      | Error (Logic (CreateSubscription.Error.AlreadyActive _)) -> Expect.isEmpty dependencies.Log "Expected no call"
      | other -> failtestf "Expected already active, got %A" other
    }

    test "a recent pending subscription sends nothing and writes nothing" {
      let dependencies = FakeDependencies (Some (subscription (SubscriptionStatus.Pending (utcNow.AddMinutes(-5.0)))), false)

      match subscribe dependencies with
      | Error (Logic (CreateSubscription.Error.ConfirmationRecent _)) -> Expect.isEmpty dependencies.Log "Expected no call"
      | other -> failtestf "Expected a recent confirmation, got %A" other
    }
  ]

[<Tests>]
let tests = testList "CreateSubscription" [ decideTests; actionTests ]
