module TwoPoint.Core.Tests.DashboardTests

open TwoPoint.Core.Dashboard.Queries
open TwoPoint.Core.Deploys
open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Queries
open TwoPoint.Core.Subscriptions
open TwoPoint.Core.Subscriptions.Queries
open TwoPoint.Core.Tests.Fixtures

open Expecto
open FsToolkit.ErrorHandling

open System
open System.Threading

let private utcNow = DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)

let private stored slug (publishDate : string option) : StoredPost =
  let date = publishDate |> Option.map (sprintf "publishDate: %s\n") |> Option.defaultValue ""
  { Slug = Slug.Unsafe.create slug; Text = $"---\ntitle: {slug}\n{date}---\n\nBody."; ETag = "v1" }

let private daysAgo (days : float) = utcNow.AddDays(-days)

let private reader created status = { subscription status with CreatedDate = created }

type private FakeStore
  (
    published : StoredPost list,
    working : StoredPost list,
    deploys : DeployContext,
    migrated : string list,
    started : string list,
    subscriptions : Subscription list
  ) =
  interface GetAllPosts.Dependencies with
    member _.GetUtcNow () = utcNow
    member _.GetPublishedPosts () = CancellableTaskResult.singleton published
    member _.GetWorkingPosts () = CancellableTaskResult.singleton working
    member _.GetPostsInTable () = CancellableTaskResult.singleton []
    member _.GetDeployContext () = CancellableTaskResult.singleton deploys
    member _.GetMigratedPosts () = migrated |> List.map Slug.Unsafe.create |> Set.ofList |> CancellableTaskResult.singleton
    member _.GetDeliveryStarts () =
      started |> List.map (fun slug -> Slug.Unsafe.create slug, utcNow) |> Map.ofList |> CancellableTaskResult.singleton
  interface GetAllSubscriptions.Dependencies with
    member _.GetAllSubscriptions () = CancellableTaskResult.singleton subscriptions

let private countsFrom (store : FakeStore) =
  match (GetDashboard.withDependencies store store () CancellationToken.None).Result with
  | Ok counts -> counts
  | Error error -> failtestf "Expected the counts, got %A" error

let private posts published working = countsFrom (FakeStore(published, working, noDeploys, [], [], []))

let private audience subscriptions = countsFrom (FakeStore([], [], noDeploys, [], [], subscriptions))

[<Tests>]
let tests =
  testList "GetDashboard" [
    test "a post with a working blob and no published blob is a draft" {
      let counts = posts [ stored "published-post" (Some "2026-01-01T00:00:00Z") ] [ stored "draft-one" None; stored "draft-two" None ]
      Expect.equal counts.Drafts 2 "The two drafts"
    }

    test "a published post with a working blob is a pending edit" {
      let counts =
        posts
          [ stored "edited-post" (Some "2026-01-01T00:00:00Z"); stored "published-post" (Some "2026-01-02T00:00:00Z") ]
          [ stored "edited-post" None; stored "draft-post" None ]

      Expect.equal counts.PendingEdits 1 "The edited post"
    }

    test "a deployed post with a past publish date, no delivery start, and no migrated mark is a delivery to start" {
      let latest = post "deploying-post" "2026-02-01T00:00:00.0000000Z"
      let published =
        [ stored "to-start" (Some "2026-01-01T00:00:00Z")
          stored "started-post" (Some "2026-01-02T00:00:00Z")
          stored "deploying-post" (Some "2026-01-03T00:00:00Z")
          stored "upcoming-post" (Some "2026-04-01T00:00:00Z") ]

      let counts = countsFrom (FakeStore(published, [], deploying latest, [], [ "started-post" ], []))
      Expect.equal counts.DeliveriesToStart 1 "Only the deployed post with no delivery start"
    }

    test "the 10 migrated posts never count as a delivery to start" {
      let migrated = [ for day in 1..10 -> $"migrated-post-{day}" ]
      let published = migrated |> List.mapi (fun index slug -> stored slug (Some $"2025-01-{index + 1:D2}T00:00:00Z"))
      let counts = countsFrom (FakeStore(published, [], noDeploys, migrated, [], []))
      Expect.equal counts.DeliveriesToStart 0 "No migrated post"
    }

    test "a subscription that is not canceled is active" {
      let counts = audience [ reader (daysAgo 90.0) Active; reader (daysAgo 2.0) Active; reader (daysAgo 90.0) (Canceled(daysAgo 5.0)) ]
      Expect.equal counts.ActiveSubscriptions 2 "The two active subscriptions"
    }

    test "a subscription made in the last 30 days is new, also when it is canceled" {
      let counts =
        audience
          [ reader (daysAgo 30.0) Active
            reader (daysAgo 1.0) (Canceled(daysAgo 0.5))
            reader (daysAgo 31.0) Active ]

      Expect.equal counts.NewSubscriptions 2 "Made 30 days ago and made 1 day ago"
    }

    test "a subscription canceled in the last 30 days is canceled in the window" {
      let counts =
        audience
          [ reader (daysAgo 90.0) (Canceled(daysAgo 30.0))
            reader (daysAgo 90.0) (Canceled(daysAgo 31.0))
            reader (daysAgo 90.0) Active ]

      Expect.equal counts.CanceledSubscriptions 1 "Canceled 30 days ago"
    }
  ]
