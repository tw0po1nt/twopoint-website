module TwoPoint.Core.Tests.SendNewPostDeliveryTests

open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Subscriptions
open TwoPoint.Core.Subscriptions.Actions
open TwoPoint.Core.Tests.Fixtures
open TwoPoint.Core.Util

open Expecto
open FsToolkit.ErrorHandling

open System
open System.Threading

let private decide = SendNewPostDelivery.Logic.decide subscriptionId

let private decisionTests =
  testList "Logic.decide" [
    test "a missing subscription gets no delivery" {
      Expect.equal
        (decide None [] latestPost.Id (Some latestPost))
        (Error (SendNewPostDelivery.SubscriptionNotFound subscriptionId))
        "Expected SubscriptionNotFound"
    }

    test "an inactive subscription gets no delivery" {
      let inactive = subscription false
      Expect.equal
        (decide (Some inactive) [] latestPost.Id (Some latestPost))
        (Error (SendNewPostDelivery.SubscriptionInactive inactive))
        "Expected SubscriptionInactive"
    }

    test "no delivery goes out for a post that is not in the posts table" {
      Expect.equal
        (decide (Some (subscription true)) [] latestPost.Id None)
        (Error (SendNewPostDelivery.PostNotFound latestPost.Id))
        "Expected PostNotFound"
    }

    test "an active subscription gets the post" {
      let active = subscription true
      Expect.equal
        (decide (Some active) [ delivery Welcome olderPost; delivery NewPost olderPost ] latestPost.Id (Some latestPost))
        (Ok (SendNewPostDelivery.Send (active, latestPost)))
        "Expected a delivery of the post"
    }

    test "a subscription that got the post in its welcome does not get it again" {
      let active = subscription true
      let welcome = delivery Welcome latestPost
      Expect.equal
        (decide (Some active) [ welcome ] latestPost.Id (Some latestPost))
        (Error (SendNewPostDelivery.AlreadySent (active, welcome)))
        "Expected AlreadySent"
    }

    test "a subscription that got a new-post delivery of the post does not get it again" {
      let active = subscription true
      let earlier = delivery NewPost latestPost
      Expect.equal
        (decide (Some active) [ earlier ] latestPost.Id (Some latestPost))
        (Error (SendNewPostDelivery.AlreadySent (active, earlier)))
        "Expected AlreadySent"
    }
  ]

let private newsletterTests =
  testList "Logic.prepareNewsletter" [
    test "a missing newsletter stops the delivery" {
      Expect.equal
        (SendNewPostDelivery.Logic.prepareNewsletter latestPost None)
        (Error (SendNewPostDelivery.NewsletterNotFound latestPost))
        "Expected NewsletterNotFound"
    }

    test "a newsletter without slots stops the delivery" {
      match SendNewPostDelivery.Logic.prepareNewsletter latestPost (Some "<html></html>") with
      | Error (SendNewPostDelivery.NewsletterInvalid (post, _)) -> Expect.equal post latestPost "Expected the post"
      | other -> failtestf "Expected NewsletterInvalid, got %A" other
    }
  ]

type private FakeDependencies(subscription, deliveries, post, newsletter) =
  let calls = ResizeArray<string>()
  let mutable intros = []

  member _.Calls = List.ofSeq calls
  member _.Intros : NonEmptyString option list = intros

  interface SendNewPostDelivery.Dependencies with
    member _.GetSubscriptionById _ = CancellableTaskResult.singleton subscription
    member _.GetDeliveriesForSubscription _ = CancellableTaskResult.singleton deliveries
    member _.GetPostBySlug _ = CancellableTaskResult.singleton post
    member _.GetNewsletter _ = CancellableTaskResult.singleton newsletter
    member _.GetUtcNow () = DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
    member _.SendNewPost _ _ intro _ =
      calls.Add "send"
      intros <- intros @ [ intro ]
      CancellableTaskResult.singleton "operation-1"
    member _.CreateDelivery _ deliveryType post _ createdDateUtc =
      calls.Add "record"
      CancellableTaskResult.singleton { delivery deliveryType post with CreatedDate = createdDateUtc }

let private run intro (dependencies : FakeDependencies) =
  let request =
    { NewPostDeliveryRequest.Dto.SubscriptionId = NewPostDeliveryRequest.SubscriptionId (subscriptionId.ToString())
      NewPostDeliveryRequest.Dto.Post = NewPostDeliveryRequest.Post (Slug.value latestPost.Id)
      NewPostDeliveryRequest.Dto.Intro = intro |> Option.map NewPostDeliveryRequest.Intro }
  (SendNewPostDelivery.withDependencies dependencies request CancellationToken.None).Result

let private actionTests =
  testList "withDependencies" [
    test "sends the post, then records a new-post delivery" {
      let dependencies = FakeDependencies(Some (subscription true), [], Some latestPost, Some slottedHtml)

      match run (Some "A new post") dependencies with
      | Ok (SendNewPostDelivery.Sent (_, recorded)) ->
        Expect.equal recorded.Type NewPost "Expected a new-post delivery"
        Expect.equal recorded.Post latestPost.Id "Expected the post"
      | other -> failtestf "Expected Sent, got %A" other

      Expect.equal dependencies.Calls [ "send"; "record" ] "Expected the send before the record"
      Expect.equal
        (dependencies.Intros |> List.map (Option.map NonEmptyString.value))
        [ Some "A new post" ]
        "Expected the intro of the message"
    }

    test "sends nothing when the welcome contained the post" {
      let dependencies =
        FakeDependencies(Some (subscription true), [ delivery Welcome latestPost ], Some latestPost, Some slottedHtml)

      match run None dependencies with
      | Error (ActionError.Logic (SendNewPostDelivery.AlreadySent _)) -> ()
      | other -> failtestf "Expected AlreadySent, got %A" other

      Expect.isEmpty dependencies.Calls "Expected no send and no record"
    }

    test "sends nothing to an inactive subscription" {
      let dependencies = FakeDependencies(Some (subscription false), [], Some latestPost, Some slottedHtml)

      match run None dependencies with
      | Error (ActionError.Logic (SendNewPostDelivery.SubscriptionInactive _)) -> ()
      | other -> failtestf "Expected SubscriptionInactive, got %A" other

      Expect.isEmpty dependencies.Calls "Expected no send and no record"
    }

    test "sends nothing when the newsletter has no slots" {
      let dependencies = FakeDependencies(Some (subscription true), [], Some latestPost, Some "<html></html>")

      match run None dependencies with
      | Error (ActionError.Logic (SendNewPostDelivery.NewsletterInvalid _)) -> ()
      | other -> failtestf "Expected NewsletterInvalid, got %A" other

      Expect.isEmpty dependencies.Calls "Expected no send and no record"
    }
  ]

[<Tests>]
let tests = testList "SendNewPostDelivery" [ decisionTests; newsletterTests; actionTests ]
