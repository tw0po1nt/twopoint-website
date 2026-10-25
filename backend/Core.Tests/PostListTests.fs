module TwoPoint.Core.Tests.PostListTests

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

type private FakeStore(published : StoredPost list, working : StoredPost list, table : PostInfo list) =
  interface GetAllPosts.Dependencies with
    member _.GetUtcNow () = utcNow
    member _.GetPublishedPosts () = CancellableTaskResult.singleton published
    member _.GetWorkingPosts () = CancellableTaskResult.singleton working
    member _.GetPostsInTable () = CancellableTaskResult.singleton table

let private list published working table =
  match (GetAllPosts.withDependencies (FakeStore(published, working, table)) () CancellationToken.None).Result with
  | Ok listings -> listings
  | Error error -> failtestf "Expected a list, got %A" error

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

    test "the list shows the drafts first, and then the newest post first" {
      let slugs =
        list [ published; edited; upcoming ] [ draft; edit ] []
        |> List.map (_.Slug >> Slug.value)

      Expect.equal slugs [ "draft-post"; "upcoming-post"; "edited-post"; "published-post" ] "The order"
    }
  ]
