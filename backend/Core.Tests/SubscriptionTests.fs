module TwoPoint.Core.Tests.SubscriptionTests

open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
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

/// Each read gives the next row of `rows`. A write succeeds only after the read of the last row, with its ETag.
type private FakeConfirmDependencies(rows : Subscription option list) =
  let saved = ResizeArray<Subscription * string>()
  let reads = ResizeArray<SubscriptionId>()
  let read index = rows |> List.item (min index (rows.Length - 1)) |> Option.map (fun row -> { Subscription = row; ETag = $"etag-{index}" })

  new(existing : Subscription option) = FakeConfirmDependencies [ existing ]

  member _.Saved = saved |> Seq.map fst |> List.ofSeq
  member _.SavedETags = saved |> Seq.map snd |> List.ofSeq
  member _.Reads = List.ofSeq reads

  interface ConfirmSubscription.Dependencies with
    member _.GetStoredSubscription id =
      reads.Add id
      CancellableTaskResult.singleton (read (reads.Count - 1))
    member _.ReplaceSubscription subscription ifMatch =
      let latest = reads.Count - 1
      if latest >= rows.Length - 1 && ifMatch = $"etag-{latest}" then
        saved.Add((subscription, ifMatch))
        CancellableTaskResult.singleton (Written $"etag-{latest}-written")
      else
        CancellableTaskResult.singleton ConditionFailed

let private confirm (dependencies : FakeConfirmDependencies) (text : string) =
  (ConfirmSubscription.withDependencies dependencies text CancellationToken.None).Result

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
      let dependencies = FakeCancelDependencies (Some (subscription Active))

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
      let existing = subscription canceledStatus
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
      let existing = subscription Active
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

    test "a cancellation of a pending subscription cancels it, so its confirm link stops working" {
      let dependencies = FakeCancelDependencies (Some (subscription (SubscriptionStatus.Pending (utcNow.AddDays(-1.0)))))

      match cancel dependencies with
      | Ok (CancelSubscription.Event.Cancelled canceled) ->
        Expect.equal canceled.Status (Canceled utcNow) "Expected the date of the cancellation"
        Expect.equal dependencies.Saved [ canceled ] "Expected one save"
      | other -> failtestf "Expected a cancellation, got %A" other
    }

    test "a confirmation makes a pending subscription active" {
      let pending = subscription (SubscriptionStatus.Pending (utcNow.AddDays(-1.0)))
      let dependencies = FakeConfirmDependencies (Some pending)

      match confirm dependencies (subscriptionId.ToString()) with
      | Ok (ConfirmSubscription.Event.Activated confirmed) ->
        Expect.equal confirmed { pending with Status = Active } "Expected an active subscription"
        Expect.equal dependencies.Saved [ confirmed ] "Expected one save"
        Expect.equal dependencies.SavedETags [ "etag-0" ] "Expected the write with the ETag of the read"
      | other -> failtestf "Expected an activation, got %A" other
    }

    test "a confirmation that another confirmation passes after the read answers confirmed and saves nothing" {
      let pending = subscription (SubscriptionStatus.Pending (utcNow.AddDays(-1.0)))
      let dependencies = FakeConfirmDependencies [ Some pending; Some { pending with Status = Active } ]

      match confirm dependencies (subscriptionId.ToString()) with
      | Ok (ConfirmSubscription.Event.NoChange SubscriptionConfirmState.Confirmed) ->
        Expect.isEmpty dependencies.Saved "Expected no save, so no second welcome"
        Expect.equal dependencies.Reads.Length 2 "Expected a second read after the failed write"
      | other -> failtestf "Expected confirmed, got %A" other
    }

    test "a confirmation of a row that changes after the read and stays pending reads again and activates it" {
      let pending = subscription (SubscriptionStatus.Pending (utcNow.AddDays(-1.0)))
      let resent = subscription (SubscriptionStatus.Pending utcNow)
      let dependencies = FakeConfirmDependencies [ Some pending; Some resent ]

      match confirm dependencies (subscriptionId.ToString()) with
      | Ok (ConfirmSubscription.Event.Activated confirmed) ->
        Expect.equal confirmed { resent with Status = Active } "Expected the row of the second read, active"
        Expect.equal dependencies.SavedETags [ "etag-1" ] "Expected the write with the ETag of the second read"
      | other -> failtestf "Expected an activation, got %A" other
    }

    test "a confirmation stops with an error when the row changes after each read" {
      let pending = subscription (SubscriptionStatus.Pending utcNow)
      let dependencies = FakeConfirmDependencies (List.replicate (ConfirmSubscription.attempts + 1) (Some pending))

      match confirm dependencies (subscriptionId.ToString()) with
      | Error _ ->
        Expect.isEmpty dependencies.Saved "Expected no save"
        Expect.equal dependencies.Reads.Length ConfirmSubscription.attempts "Expected one read for each attempt"
      | other -> failtestf "Expected an error, got %A" other
    }

    test "a second confirmation answers confirmed and saves nothing" {
      let dependencies = FakeConfirmDependencies (Some (subscription Active))

      match confirm dependencies (subscriptionId.ToString()) with
      | Ok (ConfirmSubscription.Event.NoChange SubscriptionConfirmState.Confirmed) ->
        Expect.isEmpty dependencies.Saved "Expected no save"
      | other -> failtestf "Expected confirmed, got %A" other
    }

    test "a confirmation of a canceled or unknown subscription answers unknown and saves nothing" {
      for existing in [ Some (subscription canceledStatus); None ] do
        let dependencies = FakeConfirmDependencies existing

        match confirm dependencies (subscriptionId.ToString()) with
        | Ok (ConfirmSubscription.Event.NoChange SubscriptionConfirmState.Unknown) ->
          Expect.isEmpty dependencies.Saved $"Expected no save for %A{existing}"
        | other -> failtestf "Expected unknown for %A, got %A" existing other
    }

    test "a confirmation with an ID that is not a GUID answers unknown and reads nothing" {
      let dependencies = FakeConfirmDependencies (Some (subscription (SubscriptionStatus.Pending utcNow)))

      match confirm dependencies "not-a-guid" with
      | Ok (ConfirmSubscription.Event.NoChange SubscriptionConfirmState.Unknown) ->
        Expect.isEmpty dependencies.Reads "Expected no read"
        Expect.isEmpty dependencies.Saved "Expected no save"
      | other -> failtestf "Expected unknown, got %A" other
    }
  ]

let private confirmEmailTests =
  let page = Uri "https://twopoint.dev/subscribe/confirm/"
  let pending = { subscription (SubscriptionStatus.Pending utcNow) with Name = NonEmptyString.create None "<a href=\"https://spam.example\">win</a>" |> ok |> Some }
  let content = ConfirmEmail.compose page utcNow pending
  let url = $"https://twopoint.dev/subscribe/confirm/?subscriptionId={subscriptionId}"

  testList "ConfirmEmail.compose" [
    test "the email links to the confirm page with the subscription ID" {
      Expect.stringContains content.Html $"href=\"{url}\"" "Expected the link in the HTML"
      Expect.stringContains content.Text url "Expected the link in the plain text"
    }

    test "the email contains no text from the form, so a name from a script does not reach the inbox" {
      for part in [ content.Subject; content.Html; content.Text ] do
        Expect.isFalse (part.Contains "spam.example") "Expected no name"
        Expect.isFalse (part.Contains "reader@example.com") "Expected no address"
    }

    test "the subject names the subscription" {
      Expect.equal content.Subject "Confirm your TwoPoint subscription" "Expected the subject"
    }
  ]

let private otherId = SubscriptionId.create "8d0b7a3e-1c2f-4e5a-9b6d-7f8e9a0b1c2d" |> ok

let private older = { subscription Active with Id = otherId; CreatedDate = DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc) }
let private newer = subscription canceledStatus

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
    confirmEmailTests
    queryTests
  ]
