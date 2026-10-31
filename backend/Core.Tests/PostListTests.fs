module TwoPoint.Core.Tests.PostListTests

open TwoPoint.Core.Deploys
open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Queries
open TwoPoint.Core.Tests.Fixtures
open TwoPoint.Core.Util

open Expecto
open FsToolkit.ErrorHandling
open IcedTasks

open System
open System.Threading

let private utcNow = DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)

let private stored slug text : StoredPost = { Slug = Slug.Unsafe.create slug; Text = text; ETag = "v1" }

let private text title (publishDate : string option) =
  let date = publishDate |> Option.map (sprintf "publishDate: %s\n") |> Option.defaultValue ""
  $"---\ntitle: {title}\n{date}---\n\nBody."

type private FakeStore
  (
    published : StoredPost list,
    working : StoredPost list,
    table : PostInfo list,
    deploys : DeployContext,
    migrated : string list,
    started : string list
  ) =
  interface GetAllPosts.Dependencies with
    member _.GetUtcNow () = utcNow
    member _.GetPublishedPosts () = CancellableTaskResult.singleton published
    member _.GetWorkingPosts () = CancellableTaskResult.singleton working
    member _.GetPostsInTable () = CancellableTaskResult.singleton table
    member _.GetDeployContext () = CancellableTaskResult.singleton deploys
    member _.GetMigratedPosts () = migrated |> List.map Slug.Unsafe.create |> Set.ofList |> CancellableTaskResult.singleton
    member _.GetDeliveryStarts () =
      started |> List.map (fun slug -> Slug.Unsafe.create slug, utcNow) |> Map.ofList |> CancellableTaskResult.singleton

let private listFrom (store : FakeStore) =
  match (GetAllPosts.withDependencies store () CancellationToken.None).Result with
  | Ok listings -> listings
  | Error error -> failtestf "Expected a list, got %A" error

let private listWith deploys published working table = listFrom (FakeStore(published, working, table, deploys, [], []))

let private list published working table = listWith noDeploys published working table

let private find slug (listings : PostListing list) =
  listings |> List.find (fun listing -> listing.Slug = Slug.Unsafe.create slug)

let private published = stored "published-post" (text "Published post" (Some "2026-01-01T00:00:00Z"))
let private edited = stored "edited-post" (text "Edited post" (Some "2026-01-02T00:00:00Z"))
let private upcoming = stored "upcoming-post" (text "Upcoming post" (Some "2026-04-01T00:00:00Z"))
let private draft = stored "draft-post" (text "Draft post" None)
let private edit = stored "edited-post" (text "Edited post, new title" None)

[<Tests>]
let tests =
  testList "GetAllPosts" [
    test "a post with a working blob only is a draft with no publish date" {
      let listing = list [] [ draft ] [] |> find "draft-post"
      Expect.equal listing.State PostState.Draft "Draft"
      Expect.isFalse listing.HasPendingEdit "No pending edit"
      Expect.equal listing.PublishDate None "No publish date"
      Expect.equal listing.Title "Draft post" "The title of the working blob"
    }

    test "a draft with a publish date in its front matter has no publish date" {
      let datedDraft = stored "dated-draft" (text "Dated draft" (Some "2026-02-01T00:00:00Z"))
      let listing = list [] [ datedDraft ] [] |> find "dated-draft"
      Expect.equal listing.State PostState.Draft "Draft"
      Expect.equal listing.PublishDate None "No publish date"
    }

    test "a post with a published blob and a past publish date is published" {
      let listing = list [ published ] [] [] |> find "published-post"
      Expect.equal listing.State PostState.Published "Published"
      Expect.isFalse listing.HasPendingEdit "No pending edit"
      Expect.equal listing.PublishDate (Some(DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))) "The publish date"
    }

    test "a published post with a working blob has a pending edit and keeps its published title" {
      let listing = list [ edited ] [ edit ] [] |> find "edited-post"
      Expect.equal listing.State PostState.Published "Still published"
      Expect.isTrue listing.HasPendingEdit "A pending edit"
      Expect.equal listing.Title "Edited post" "The published title"
    }

    test "a published post with a future publish date is upcoming" {
      let listing = list [ upcoming ] [] [] |> find "upcoming-post"
      Expect.equal listing.State PostState.Upcoming "Upcoming"
    }

    test "a post published at this moment is published" {
      let now = stored "now-post" (text "Now" (Some "2026-03-01T00:00:00Z"))
      Expect.equal (list [ now ] [] [] |> find "now-post").State PostState.Published "Published"
    }

    test "a post gets the comment counts of its table row, and zero counts without a row" {
      let info = (post "published-post" "2026-01-01T00:00:00.0000000Z").Info
      let counts = { New = 2u; Approved = 3u; Rejected = 1u }
      let listings = list [ published; upcoming ] [] [ { info with CommentStats = counts } ]
      Expect.equal (find "published-post" listings).CommentStats counts "The counts of the row"
      Expect.equal (find "upcoming-post" listings).CommentStats PostCommentStats.Zero "No row"
    }

    test "a blob with no title gets its slug as title" {
      let untitled = stored "untitled-post" "No front matter."
      Expect.equal (list [] [ untitled ] [] |> find "untitled-post").Title "untitled-post" "The slug"
    }

    test "a post with a failed dispatch shows a failed deploy, and a post with no change shows a deployed state" {
      let failed = { ChangedDate = utcNow; Production = ProductionDispatch.Failed }
      let deploys = { noDeploys with Changes = Map.ofList [ Slug.Unsafe.create "published-post", failed ] }
      let listings = listWith deploys [ published; upcoming ] [] []

      Expect.equal (find "published-post" listings).Deploy DeployFailed "The failed dispatch"
      Expect.equal (find "upcoming-post" listings).Deploy Deployed "No change is recorded"
    }

    test "a published post that is deployed, with no delivery start and no migrated mark, is a delivery to start" {
      let listings = list [ published; upcoming ] [ draft ] []
      Expect.isTrue (find "published-post" listings).DeliveryToStart "The published post"
      Expect.isFalse (find "upcoming-post" listings).DeliveryToStart "Not before the publish date"
      Expect.isFalse (find "draft-post" listings).DeliveryToStart "Not a draft"
    }

    test "a post is not a delivery to start before the deploy of its latest change" {
      let row = post "published-post" "2026-01-01T00:00:00.0000000Z"
      Expect.isFalse (listWith (deploying row) [ published ] [] [] |> find "published-post").DeliveryToStart "Deploying"
      Expect.isFalse (listWith (deployFailed row) [ published ] [] [] |> find "published-post").DeliveryToStart "Failed"
    }

    test "a post with a delivery start or the migrated mark is not a delivery to start" {
      let listings =
        listFrom (FakeStore([ published; edited ], [], [], noDeploys, [ "published-post" ], [ "edited-post" ]))

      Expect.isFalse (find "published-post" listings).DeliveryToStart "The migrated mark"
      Expect.isFalse (find "edited-post" listings).DeliveryToStart "A delivery start"
    }

    test "the list shows the drafts first, and then the newest post first" {
      let slugs =
        list [ published; edited; upcoming ] [ draft; edit ] []
        |> List.map (_.Slug >> Slug.value)

      Expect.equal slugs [ "draft-post"; "upcoming-post"; "edited-post"; "published-post" ] "The order"
    }
  ]
