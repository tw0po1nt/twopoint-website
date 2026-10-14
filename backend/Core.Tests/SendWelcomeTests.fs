module TwoPoint.Core.Tests.SendWelcomeTests

open TwoPoint.Core.Posts
open TwoPoint.Core.Subscriptions
open TwoPoint.Core.Subscriptions.Actions
open TwoPoint.Core.Tests.Fixtures
open TwoPoint.Core.Util

open Expecto
open FsToolkit.ErrorHandling

open System
open System.Threading

let private decide = SendWelcome.Logic.decide subscriptionId

let private decisionTests =
  testList "Logic.decide" [
    test "a missing subscription gets no welcome" {
      Expect.equal
        (decide None [] (Some latestPost))
        (Error (SendWelcome.SubscriptionNotFound subscriptionId))
        "Expected SubscriptionNotFound"
    }

    test "an inactive subscription gets no welcome" {
      let inactive = subscription false
      Expect.equal
        (decide (Some inactive) [] (Some latestPost))
        (Error (SendWelcome.SubscriptionInactive inactive))
        "Expected SubscriptionInactive"
    }

    test "no welcome goes out when there are no posts" {
      Expect.equal (decide (Some (subscription true)) [] None) (Error SendWelcome.NoPosts) "Expected NoPosts"
    }

    test "a new subscription gets the latest post" {
      let active = subscription true
      Expect.equal
        (decide (Some active) [] (Some latestPost))
        (Ok (SendWelcome.Send (active, latestPost)))
        "Expected a welcome with the latest post"
    }

    test "a reactivation gets no second welcome for the same post" {
      let active = subscription true
      let earlierWelcome = delivery Welcome latestPost
      Expect.equal
        (decide (Some active) [ earlierWelcome ] (Some latestPost))
        (Error (SendWelcome.AlreadySent (active, earlierWelcome)))
        "Expected AlreadySent"
    }

    test "a reactivation gets a welcome when a newer post exists" {
      let active = subscription true
      Expect.equal
        (decide (Some active) [ delivery Welcome olderPost ] (Some latestPost))
        (Ok (SendWelcome.Send (active, latestPost)))
        "Expected a welcome with the newer post"
    }

    test "a new-post delivery of the latest post does not count as a welcome" {
      let active = subscription true
      Expect.equal
        (decide (Some active) [ delivery NewPost latestPost ] (Some latestPost))
        (Ok (SendWelcome.Send (active, latestPost)))
        "Expected a welcome"
    }
  ]

let private newsletterTests =
  testList "Logic.prepareNewsletter" [
    test "a missing newsletter stops the welcome" {
      Expect.equal
        (SendWelcome.Logic.prepareNewsletter latestPost None)
        (Error (SendWelcome.NewsletterNotFound latestPost))
        "Expected NewsletterNotFound"
    }

    test "a newsletter without slots stops the welcome" {
      match SendWelcome.Logic.prepareNewsletter latestPost (Some "<html></html>") with
      | Error (SendWelcome.NewsletterInvalid (post, _)) -> Expect.equal post latestPost "Expected the latest post"
      | other -> failtestf "Expected NewsletterInvalid, got %A" other
    }

    test "a newsletter with slots is ready to send" {
      Expect.isOk (SendWelcome.Logic.prepareNewsletter latestPost (Some slottedHtml)) "Expected a newsletter"
    }
  ]

type private FakeDependencies(subscription, deliveries, latestPost, newsletter) =
  let calls = ResizeArray<string>()

  member _.Calls = List.ofSeq calls

  interface SendWelcome.Dependencies with
    member _.GetSubscriptionById _ = CancellableTaskResult.singleton subscription
    member _.GetDeliveriesForSubscription _ = CancellableTaskResult.singleton deliveries
    member _.GetLatestPost () = CancellableTaskResult.singleton latestPost
    member _.GetNewsletter _ = CancellableTaskResult.singleton newsletter
    member _.GetUtcNow () = DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
    member _.SendWelcome _ _ =
      calls.Add "send"
      CancellableTaskResult.singleton "operation-1"
    member _.CreateDelivery _ deliveryType post _ createdDateUtc =
      calls.Add "record"
      CancellableTaskResult.singleton { delivery deliveryType post with CreatedDate = createdDateUtc }

let private run (dependencies : FakeDependencies) =
  let request = { WelcomeRequest.Dto.SubscriptionId = WelcomeRequest.SubscriptionId (subscriptionId.ToString()) }
  (SendWelcome.withDependencies dependencies request CancellationToken.None).Result

let private actionTests =
  testList "withDependencies" [
    test "sends the welcome, then records the delivery" {
      let dependencies = FakeDependencies(Some (subscription true), [], Some latestPost, Some slottedHtml)

      match run dependencies with
      | Ok (SendWelcome.Sent (_, recorded)) ->
        Expect.equal recorded.Type Welcome "Expected a welcome delivery"
        Expect.equal recorded.Post latestPost.Id "Expected the latest post"
      | other -> failtestf "Expected Sent, got %A" other

      Expect.equal dependencies.Calls [ "send"; "record" ] "Expected the send before the record"
    }

    test "sends nothing when a welcome for the latest post exists" {
      let dependencies =
        FakeDependencies(Some (subscription true), [ delivery Welcome latestPost ], Some latestPost, Some slottedHtml)

      match run dependencies with
      | Error (ActionError.Logic (SendWelcome.AlreadySent _)) -> ()
      | other -> failtestf "Expected AlreadySent, got %A" other

      Expect.isEmpty dependencies.Calls "Expected no send and no record"
    }

    test "sends nothing when the newsletter has no slots" {
      let dependencies = FakeDependencies(Some (subscription true), [], Some latestPost, Some "<html></html>")

      match run dependencies with
      | Error (ActionError.Logic (SendWelcome.NewsletterInvalid _)) -> ()
      | other -> failtestf "Expected NewsletterInvalid, got %A" other

      Expect.isEmpty dependencies.Calls "Expected no send and no record"
    }
  ]

[<Tests>]
let tests = testList "SendWelcome" [ decisionTests; newsletterTests; actionTests ]
