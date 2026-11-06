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
  match model.Router.Route with
  | Posts filter -> Posts.shown filter model.Posts |> List.map _.Slug
  | _ -> []

let private follow (path: string) (model: Shell.Model) =
  Shell.update (Shell.Router(Router.LinkFollowed path)) model

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
      Expect.equal model.Router.Route (Posts PostFilter.All) "All at first"
      Expect.equal (shown model) [ "draft"; "published"; "edited"; "upcoming" ] "All"

      for path, expected in [ "/posts?filter=drafts", [ "draft" ]; "/posts?filter=pending-edits", [ "edited" ] ] do
        let model, effect = follow path model
        Expect.equal effect (Some(Shell.Navigate(Router.PushUrl path, []))) "The filter calls no API"
        Expect.equal (shown model) expected $"Only {path}"

      let model, _ = follow "/posts?filter=drafts" model
      let model, _ = follow "/posts" model
      Expect.equal (shown model) [ "draft"; "published"; "edited"; "upcoming" ] "All again"
    }

    test "a start at a filter shows that filter" {
      let model, _ = start "/posts?filter=drafts"
      let model, _ = Shell.update (Shell.Posts(Posts.Answered(Ok [ draft; published; edited; upcoming ]))) model
      Expect.equal model.Router.Route (Posts PostFilter.Drafts) "The drafts"
      Expect.equal (shown model) [ "draft" ] "Only the draft"
    }

    test "Back to the list shows the filter of the URL, and the list loads again" {
      let model, _ = follow "/posts?filter=pending-edits" (listed ())
      let model, _ = follow "/comments" model
      let model, effect = Shell.update (Shell.Router(Router.UrlChanged "/posts?filter=pending-edits")) model
      Expect.equal effect (Some(Shell.Send [ Shell.LoadPosts ])) "The posts again"
      Expect.equal model.Router.Route (Posts PostFilter.PendingEdits) "The pending edits"
      Expect.equal (shown model) [ "edited" ] "The rows of the last load stay until the answer"
    }

    test "a list that did not load waits again on a back" {
      let model, _ = start "/posts"
      let model, _ = Shell.update (Shell.Posts(Posts.Answered(Error Api.Unanswered))) model
      let model, _ = follow "/comments" model
      let model, _ = follow "/posts" model
      Expect.equal model.Posts.Posts Posts.Waiting "The list waits"
    }

    test "the navigation link opens the list with All" {
      let model, _ = follow "/posts?filter=pending-edits" (listed ())
      let model, _ = follow "/comments" model
      let model, _ = follow (Route.path (List.item 1 Router.navigation)) model
      Expect.equal model.Router.Route (Posts PostFilter.All) "All"
    }

    test "the filter of deliveries to start shows only the posts that wait for a delivery" {
      let model, _ = start "/posts"
      let model, _ = Shell.update (Shell.Posts(Posts.Answered(Ok [ draft; published; toSend; upcoming ]))) model
      let model, effect = follow "/posts?filter=pending-deliveries" model
      Expect.equal effect (Some(Shell.Navigate(Router.PushUrl "/posts?filter=pending-deliveries", []))) "The filter calls no API"
      Expect.equal (shown model) [ "to-send" ] "Only the post to send"
      Expect.contains (Posts.filters |> List.map snd) "Pending deliveries" "The filter tab"
      Expect.equal (Posts.emptyText PostFilter.PendingDeliveries) "No pending deliveries." "The empty text"
    }

    test "a filter with no post has its own empty text" {
      Expect.equal (Posts.emptyText PostFilter.All) "No posts yet." "All"
      Expect.equal (Posts.emptyText PostFilter.Drafts) "No drafts." "Drafts"
      Expect.equal (Posts.emptyText PostFilter.PendingEdits) "No pending edits." "Pending edits"
    }

    test "a failed load shows the reason, and an answer of 403 refuses the account" {
      let model, _ = start "/posts"
      let failed, _ = Shell.update (Shell.Posts(Posts.Answered(Error Api.Unanswered))) model
      Expect.equal failed.Posts.Posts (Posts.NotLoaded(Api.reason Api.Unanswered)) "The reason"
      Expect.isEmpty (Posts.shown PostFilter.All failed.Posts) "No post"

      let refused, _ = Shell.update (Shell.Posts(Posts.Answered(Error Api.Refused))) model
      Expect.equal refused.Session.Status Session.Refused "The account is refused"
    }
  ]
