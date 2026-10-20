module TwoPoint.Core.Tests.StartNewPostDeliveryTests

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

let private utcNow = DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
let private upcomingPost = post "upcoming-post" "2026-04-01T00:00:00.0000000Z"

let private decide = StartNewPostDelivery.Logic.decide utcNow

let private decisionTests =
  testList "Logic.decide" [
    test "a post that is not in the posts table gets no delivery" {
      Expect.equal
        (decide latestPost.Id None)
        (Error (StartNewPostDelivery.PostNotFound latestPost.Id))
        "Expected PostNotFound"
    }

    test "an upcoming post gets no delivery" {
      Expect.equal
        (decide upcomingPost.Id (Some upcomingPost))
        (Error (StartNewPostDelivery.PostUpcoming upcomingPost))
        "Expected PostUpcoming"
    }

    test "a published post gets a delivery" {
      Expect.equal
        (decide latestPost.Id (Some latestPost))
        (Ok (StartNewPostDelivery.Start latestPost))
        "Expected a start"
    }

    test "a post published at this moment gets a delivery" {
      let now = post "now-post" "2026-03-01T00:00:00.0000000Z"
      Expect.equal (decide now.Id (Some now)) (Ok (StartNewPostDelivery.Start now)) "Expected a start"
    }
  ]

let private newsletterTests =
  testList "Logic.prepareNewsletter" [
    test "a missing newsletter stops the delivery" {
      Expect.equal
        (StartNewPostDelivery.Logic.prepareNewsletter latestPost None)
        (Error (StartNewPostDelivery.NewsletterNotFound latestPost))
        "Expected NewsletterNotFound"
    }

    test "a newsletter without slots stops the delivery" {
      match StartNewPostDelivery.Logic.prepareNewsletter latestPost (Some "<html></html>") with
      | Error (StartNewPostDelivery.NewsletterInvalid (post, _)) -> Expect.equal post latestPost "Expected the post"
      | other -> failtestf "Expected NewsletterInvalid, got %A" other
    }
  ]

let private otherSubscription =
  { subscription true with
      Id = SubscriptionId.create "8d0b7a3e-1c2f-4e5a-9b6d-7f8e9a0b1c2d" |> ok
      EmailAddress = EmailAddress.create "other@example.com" |> ok }

let private recipientTests =
  testList "Logic.recipients" [
    test "only active subscriptions get a delivery" {
      Expect.equal
        (StartNewPostDelivery.Logic.recipients [ subscription true; subscription false; otherSubscription ])
        [ subscription true; otherSubscription ]
        "Expected the active subscriptions"
    }
  ]

type private FakeDependencies(post, newsletter, subscriptions) =
  let queued = ResizeArray<Post * NonEmptyString option * Subscription list>()

  member _.Queued = List.ofSeq queued

  interface StartNewPostDelivery.Dependencies with
    member _.GetPostBySlug _ = CancellableTaskResult.singleton post
    member _.GetNewsletter _ = CancellableTaskResult.singleton newsletter
    member _.GetAllSubscriptions () = CancellableTaskResult.singleton subscriptions
    member _.GetUtcNow () = utcNow
    member _.QueueDeliveries post intro recipients =
      queued.Add (post, intro, recipients)
      CancellableTaskResult.singleton ()

let private run intro (dependencies : FakeDependencies) =
  let start =
    { NewPostDeliveryStart.Dto.Post = NewPostDeliveryStart.Post (Slug.value latestPost.Id)
      NewPostDeliveryStart.Dto.Intro = intro |> Option.map NewPostDeliveryStart.Intro }
  (StartNewPostDelivery.withDependencies dependencies start CancellationToken.None).Result

let private allSubscriptions = [ subscription true; subscription false; otherSubscription ]

let private actionTests =
  testList "withDependencies" [
    test "queues one delivery for each active subscription" {
      let dependencies = FakeDependencies(Some latestPost, Some slottedHtml, allSubscriptions)

      match run (Some "Hello") dependencies with
      | Ok (StartNewPostDelivery.Started (post, recipients)) ->
        Expect.equal post latestPost "Expected the post"
        Expect.equal recipients [ subscription true; otherSubscription ] "Expected the active subscriptions"
      | other -> failtestf "Expected Started, got %A" other

      match dependencies.Queued with
      | [ post, Some intro, recipients ] ->
        Expect.equal post latestPost "Expected the post"
        Expect.equal (NonEmptyString.value intro) "Hello" "Expected the intro"
        Expect.equal recipients [ subscription true; otherSubscription ] "Expected the active subscriptions"
      | other -> failtestf "Expected one queue call, got %A" other
    }

    test "a blank intro queues no intro" {
      let dependencies = FakeDependencies(Some latestPost, Some slottedHtml, allSubscriptions)

      run (Some "  \n  ") dependencies |> ignore

      match dependencies.Queued with
      | [ _, None, _ ] -> ()
      | other -> failtestf "Expected one queue call with no intro, got %A" other
    }

    test "queues nothing for a post that is not in the posts table" {
      let dependencies = FakeDependencies(None, Some slottedHtml, allSubscriptions)

      match run None dependencies with
      | Error (ActionError.Logic (StartNewPostDelivery.PostNotFound _)) -> ()
      | other -> failtestf "Expected PostNotFound, got %A" other

      Expect.isEmpty dependencies.Queued "Expected no queue message"
    }

    test "queues nothing for an upcoming post" {
      let dependencies = FakeDependencies(Some upcomingPost, Some slottedHtml, allSubscriptions)

      match run None dependencies with
      | Error (ActionError.Logic (StartNewPostDelivery.PostUpcoming _)) -> ()
      | other -> failtestf "Expected PostUpcoming, got %A" other

      Expect.isEmpty dependencies.Queued "Expected no queue message"
    }

    test "queues nothing when the newsletter is missing" {
      let dependencies = FakeDependencies(Some latestPost, None, allSubscriptions)

      match run None dependencies with
      | Error (ActionError.Logic (StartNewPostDelivery.NewsletterNotFound _)) -> ()
      | other -> failtestf "Expected NewsletterNotFound, got %A" other

      Expect.isEmpty dependencies.Queued "Expected no queue message"
    }

    test "queues nothing when the newsletter has no slots" {
      let dependencies = FakeDependencies(Some latestPost, Some "<html></html>", allSubscriptions)

      match run None dependencies with
      | Error (ActionError.Logic (StartNewPostDelivery.NewsletterInvalid _)) -> ()
      | other -> failtestf "Expected NewsletterInvalid, got %A" other

      Expect.isEmpty dependencies.Queued "Expected no queue message"
    }
  ]

[<Tests>]
let tests = testList "StartNewPostDelivery" [ decisionTests; newsletterTests; recipientTests; actionTests ]
