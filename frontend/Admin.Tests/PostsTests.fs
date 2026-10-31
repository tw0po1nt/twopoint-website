module TwoPoint.Admin.Tests.PostsTests

open TwoPoint.Admin

open Expecto

let private post slug state pendingEdit : Api.Post =
  { Slug = slug
    Title = slug
    State = state
    PendingEdit = pendingEdit
    PublishDate = None
    Deploy = Api.DeployState.Deployed
    DeliveryToStart = false
    CommentStats = { New = 0; Approved = 0; Rejected = 0 } }

let private draft = post "draft" Api.Draft false
let private published = post "published" Api.Published false
let private edited = post "edited" Api.Published true
let private upcoming = post "upcoming" Api.Upcoming false
let private toSend = { post "to-send" Api.Published false with DeliveryToStart = true }

let private start (path: string) =
  let model, _ = Shell.init path
  Shell.update (Shell.Session(Session.Started true)) model

let private listed () =
  let model, _ = start "/posts"
  let model, _ = Shell.update (Shell.Posts(Posts.Answered(Ok [ draft; published; edited; upcoming ]))) model
  model

let private shown (model: Shell.Model) =
  Posts.shown model.Posts |> List.map _.Slug

[<Tests>]
let tests =
  testList "Posts" [
    test "a start on the post list loads the posts with the first API calls" {
      let model, effect = start "/posts"
      Expect.equal effect (Some(Shell.Send [ Shell.LoadQueue; Shell.LoadPosts ])) "The posts"
      Expect.equal model.Posts.Posts Posts.Waiting "The list waits"
    }

    test "the filters show all posts, only the drafts, or only the posts with a pending edit" {
      let model = listed ()
      Expect.equal model.Posts.Filter Posts.All "All at first"
      Expect.equal (shown model) [ "draft"; "published"; "edited"; "upcoming" ] "All"

      for filter, expected in [ Posts.Drafts, [ "draft" ]; Posts.PendingEdits, [ "edited" ] ] do
        let model, effect = Shell.update (Shell.Posts(Posts.Filtered filter)) model
        Expect.isNone effect "The filter calls no API"
        Expect.equal (shown model) expected $"Only {filter}"

      let model, _ = Shell.update (Shell.Posts(Posts.Filtered Posts.Drafts)) model
      let model, _ = Shell.update (Shell.Posts(Posts.Filtered Posts.All)) model
      Expect.equal (shown model) [ "draft"; "published"; "edited"; "upcoming" ] "All again"
    }

    test "the filter stays when the author comes back to the list, and the list loads again" {
      let model, _ = Shell.update (Shell.Posts(Posts.Filtered Posts.PendingEdits)) (listed ())
      let model, _ = Shell.update (Shell.Router(Router.LinkFollowed "/comments")) model
      let model, effect = Shell.update (Shell.Router(Router.UrlChanged "/posts")) model
      Expect.equal effect (Some(Shell.Send [ Shell.LoadPosts ])) "The posts again"
      Expect.equal model.Posts.Filter Posts.PendingEdits "The pending edits"
      Expect.equal model.Posts.Posts Posts.Waiting "The list waits"
    }

    test "the filter of deliveries to start shows only the posts that wait for a delivery" {
      let model, _ = start "/posts"
      let model, _ = Shell.update (Shell.Posts(Posts.Answered(Ok [ draft; published; toSend; upcoming ]))) model
      let model, effect = Shell.update (Shell.Posts(Posts.Filtered Posts.DeliveriesToStart)) model
      Expect.isNone effect "The filter calls no API"
      Expect.equal (shown model) [ "to-send" ] "Only the post to send"
      Expect.contains (Posts.filters |> List.map snd) "Deliveries to start" "The filter tab"
      Expect.equal (Posts.emptyText Posts.DeliveriesToStart) "No post waits for a delivery." "The empty text"
    }

    test "a filter with no post has its own empty text" {
      Expect.equal (Posts.emptyText Posts.All) "The blog has no posts." "All"
      Expect.equal (Posts.emptyText Posts.Drafts) "The blog has no drafts." "Drafts"
      Expect.equal (Posts.emptyText Posts.PendingEdits) "No post has a pending edit." "Pending edits"
    }

    test "a failed load shows the reason, and an answer of 403 refuses the account" {
      let model, _ = start "/posts"
      let failed, _ = Shell.update (Shell.Posts(Posts.Answered(Error Api.Unanswered))) model
      Expect.equal failed.Posts.Posts (Posts.NotLoaded(Api.reason Api.Unanswered)) "The reason"
      Expect.isEmpty (Posts.shown failed.Posts) "No post"

      let refused, _ = Shell.update (Shell.Posts(Posts.Answered(Error Api.Refused))) model
      Expect.equal refused.Session.Status Session.Refused "The account is refused"
    }
  ]
