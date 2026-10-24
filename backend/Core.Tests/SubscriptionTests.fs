module TwoPoint.Core.Tests.SubscriptionTests

open TwoPoint.Core.Posts
open TwoPoint.Core.Subscriptions
open TwoPoint.Core.Subscriptions.Actions
open TwoPoint.Core.Subscriptions.Queries
open TwoPoint.Core.Tests.Fixtures
open TwoPoint.Core.Util

open Expecto
open FsToolkit.ErrorHandling

open System
open System.Threading

let private utcNow = DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc)

type private FakeCancelDependencies(existing : Subscription option) =
  let saved = ResizeArray<Subscription>()

  member _.Saved = List.ofSeq saved

  interface CancelSubscription.Dependencies with
    member _.GetSubscriptionByEmail _ = CancellableTaskResult.singleton existing
    member _.GetSubscriptionById _ = CancellableTaskResult.singleton existing
    member _.GetUtcNow () = utcNow
    member _.DeactivateSubscription subscription =
      saved.Add subscription
      CancellableTaskResult.singleton subscription

let private cancel (dependencies : FakeCancelDependencies) =
  let dto =
    { SubscriptionCancellation.Dto.EmailAddress = None
      SubscriptionCancellation.Dto.SubscriptionId =
        Some (SubscriptionCancellation.SubscriptionId (subscriptionId.ToString())) }
  (CancelSubscription.withDependencies dependencies dto CancellationToken.None).Result

type private FakeReactivateDependencies(existing : Subscription option) =
  let saved = ResizeArray<Subscription>()

  member _.Saved = List.ofSeq saved

  interface CreateSubscription.Dependencies with
    member _.GetSubscriptionByEmail _ = CancellableTaskResult.singleton existing
    member _.GetUtcNow () = utcNow
    member _.CreateSubscription _ _ = failwith "Expected no new subscription"
    member _.ActivateSubscription subscription =
      saved.Add subscription
      CancellableTaskResult.singleton subscription

let private subscribe (dependencies : FakeReactivateDependencies) =
  let dto =
    { NewSubscription.Dto.EmailAddress = NewSubscription.EmailAddress "reader@example.com"
      NewSubscription.Dto.Name = None }
  (CreateSubscription.withDependencies dependencies dto CancellationToken.None).Result

type private FakeDeleteDependencies(existing : Subscription option, deliveries : SubscriptionDelivery list) =
  let log = ResizeArray<string>()

  member _.Log = List.ofSeq log

  interface DeleteSubscription.Dependencies with
    member _.GetSubscriptionById _ = CancellableTaskResult.singleton existing
    member _.GetDeliveriesForSubscription _ = CancellableTaskResult.singleton deliveries
    member _.DeleteDelivery _ delivery =
      log.Add $"delivery {delivery.Id}"
      CancellableTaskResult.singleton ()
    member _.DeleteSubscription subscription =
      log.Add $"subscription {subscription.Id}"
      CancellableTaskResult.singleton ()

let private delete (dependencies : FakeDeleteDependencies) (id : string) =
  let dto = { SubscriptionDeletion.Dto.SubscriptionId = SubscriptionDeletion.SubscriptionId id }
  (DeleteSubscription.withDependencies dependencies dto CancellationToken.None).Result

let private actionTests =
  testList "Subscription actions" [
    test "a cancellation makes the subscription inactive and sets the canceled date" {
      let dependencies = FakeCancelDependencies (Some (subscription true))

      match cancel dependencies with
      | Ok (CancelSubscription.Event.Cancelled canceled) ->
        Expect.equal canceled.Status (Canceled utcNow) "Expected the date of the cancellation"
        Expect.equal dependencies.Saved [ canceled ] "Expected one save"
      | other -> failtestf "Expected a cancellation, got %A" other
    }

    test "a cancellation needs no way to send an email" {
      let sendMembers =
        typeof<CancelSubscription.Dependencies>.GetMethods() |> Array.filter (fun m -> m.Name.Contains "Send")
      Expect.isEmpty sendMembers "Expected no way to send an email"
    }

    test "a second cancellation keeps the first canceled date and saves nothing" {
      let existing = subscription false
      let dependencies = FakeCancelDependencies (Some existing)

      match cancel dependencies with
      | Ok (CancelSubscription.Event.Cancelled canceled) ->
        Expect.equal canceled.Status existing.Status "Expected the first date"
        Expect.isEmpty dependencies.Saved "Expected no save"
      | other -> failtestf "Expected a cancellation, got %A" other
    }

    test "a delete removes each delivery of the subscription, then the subscription" {
      let welcome = delivery Welcome olderPost
      let newPost = delivery NewPost latestPost
      let existing = subscription true
      let dependencies = FakeDeleteDependencies (Some existing, [ welcome; newPost ])

      match delete dependencies (subscriptionId.ToString()) with
      | Ok (DeleteSubscription.Event.Deleted deleted) ->
        Expect.equal deleted existing "Expected the deleted subscription"
        Expect.equal
          dependencies.Log
          [ $"delivery {welcome.Id}"; $"delivery {newPost.Id}"; $"subscription {existing.Id}" ]
          "Expected the deliveries before the subscription"
      | other -> failtestf "Expected a delete, got %A" other
    }

    test "a delete of an unknown subscription removes nothing" {
      let dependencies = FakeDeleteDependencies (None, [])

      match delete dependencies (subscriptionId.ToString()) with
      | Error (Logic (DeleteSubscription.Error.SubscriptionNotFound _)) ->
        Expect.isEmpty dependencies.Log "Expected no delete"
      | other -> failtestf "Expected not found, got %A" other
    }

    test "a delete with an id that is not a guid is a validation error" {
      match delete (FakeDeleteDependencies (None, [])) "not-a-guid" with
      | Error actionError -> Expect.isTrue actionError.IsValidation "Expected a validation error"
      | other -> failtestf "Expected a validation error, got %A" other
    }

    test "a reactivation makes the subscription active and clears the canceled date" {
      let dependencies = FakeReactivateDependencies (Some (subscription false))

      match subscribe dependencies with
      | Ok (CreateSubscription.Event.Reactivated reactivated) ->
        Expect.equal reactivated.Status Active "Expected an active subscription"
        Expect.equal dependencies.Saved [ reactivated ] "Expected one save"
      | other -> failtestf "Expected a reactivation, got %A" other
    }
  ]

let private otherId = SubscriptionId.create "8d0b7a3e-1c2f-4e5a-9b6d-7f8e9a0b1c2d" |> ok

let private older = { subscription true with Id = otherId; CreatedDate = DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc) }
let private newer = subscription false

type private FakeListDependencies(subscriptions : Subscription list) =
  interface GetAllSubscriptions.Dependencies with
    member _.GetAllSubscriptions () = CancellableTaskResult.singleton subscriptions

type private FakeDeliveriesDependencies(existing : Subscription option, deliveries : SubscriptionDelivery list, posts : Post list) =
  interface GetSubscriptionDeliveries.Dependencies with
    member _.GetSubscriptionById _ = CancellableTaskResult.singleton existing
    member _.GetDeliveriesForSubscription _ = CancellableTaskResult.singleton deliveries
    member _.GetPostsBySlugs _ = CancellableTaskResult.singleton posts

let private deliveriesOf (dependencies : FakeDeliveriesDependencies) (id : string) =
  (GetSubscriptionDeliveries.withDependencies dependencies id CancellationToken.None).Result

let private queryTests =
  testList "Subscription queries" [
    test "the subscriptions come newest first" {
      let query = GetAllSubscriptions.withDependencies (FakeListDependencies [ older; newer ])

      match (query () CancellationToken.None).Result with
      | Ok subscriptions -> Expect.equal subscriptions [ newer; older ] "Expected the newest first"
      | other -> failtestf "Expected Ok, got %A" other
    }

    test "the deliveries come newest first, each with the title of its post" {
      let welcome = { delivery Welcome olderPost with CreatedDate = DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc) }
      let newPost = { delivery NewPost latestPost with CreatedDate = DateTime(2026, 2, 5, 0, 0, 0, DateTimeKind.Utc) }
      let dependencies = FakeDeliveriesDependencies (Some newer, [ welcome; newPost ], [ olderPost; latestPost ])

      match deliveriesOf dependencies (subscriptionId.ToString()) with
      | Ok (Some delivered) ->
        Expect.equal (delivered |> List.map _.Delivery) [ newPost; welcome ] "Expected the newest first"
        Expect.equal (delivered |> List.map _.PostTitle) [ Some latestPost.Info.Title; Some olderPost.Info.Title ] "Expected the titles"
      | other -> failtestf "Expected the deliveries, got %A" other
    }

    test "a delivery of a post that is gone has no title" {
      let dependencies = FakeDeliveriesDependencies (Some newer, [ delivery NewPost latestPost ], [])

      match deliveriesOf dependencies (subscriptionId.ToString()) with
      | Ok (Some [ delivered ]) -> Expect.isNone delivered.PostTitle "Expected no title"
      | other -> failtestf "Expected one delivery, got %A" other
    }

    test "an unknown subscription has no deliveries" {
      let dependencies = FakeDeliveriesDependencies (None, [], [])

      Expect.equal (deliveriesOf dependencies (subscriptionId.ToString())) (Ok None) "Expected None"
    }

    test "an id that is not a guid is a validation error" {
      let dependencies = FakeDeliveriesDependencies (None, [], [])

      match deliveriesOf dependencies "not-a-guid" with
      | Error (QueryError.Validation _) -> ()
      | other -> failtestf "Expected a validation error, got %A" other
    }
  ]

[<Tests>]
let tests =
  testList "Subscriptions" [
    actionTests
    queryTests
  ]
