module TwoPoint.Admin.Tests.RouteTests

open TwoPoint.Admin

open Expecto

let private routes =
  [ Dashboard, "/"
    Posts PostFilter.All, "/posts"
    Posts PostFilter.Drafts, "/posts?filter=drafts"
    Posts PostFilter.PendingEdits, "/posts?filter=pending-edits"
    Posts PostFilter.DeliveriesToStart, "/posts?filter=deliveries-to-start"
    Post "fable-reminders-part-2", "/posts/fable-reminders-part-2"
    PostComments "fable-reminders-part-2", "/posts/fable-reminders-part-2/comments"
    ModerationQueue, "/comments"
    Commenters, "/commenters"
    Commenter "7d1f0c55-3c1b-4a43-9a0e-2b5f6f0d8e21", "/commenters/7d1f0c55-3c1b-4a43-9a0e-2b5f6f0d8e21"
    Series, "/series"
    Subscriptions SubscriptionFilter.All, "/subscriptions"
    Subscriptions SubscriptionFilter.Active, "/subscriptions?filter=active"
    Subscriptions SubscriptionFilter.Inactive, "/subscriptions?filter=inactive"
    Settings, "/settings" ]

[<Tests>]
let tests =
  testList "Route" [
    test "each route has one path, and the path gives the route again" {
      for route, path in routes do
        Expect.equal (Route.path route) path $"The path of {route}"
        Expect.equal (Route.parse path) route $"The route of {path}"
    }

    test "a path with a slash at the end gives the same route" {
      Expect.equal (Route.parse "/posts/") (Posts PostFilter.All) "The posts"
      Expect.equal (Route.parse "/posts/hello/") (Post "hello") "A post"
    }

    test "the slug of a post is decoded from the path, and encoded into it" {
      Expect.equal (Route.parse "/posts/hello%20world") (Post "hello world") "The slug is decoded"
      Expect.equal (Route.path (Post "hello world")) "/posts/hello%20world" "The slug is encoded"
      Expect.equal (Route.parse "/posts/hello%20world/comments") (PostComments "hello world") "The comments decode it"
      Expect.equal (Route.path (PostComments "hello world")) "/posts/hello%20world/comments" "The comments encode it"
    }

    test "each filter of the two pages has a path, and the path gives the filter again" {
      for filter in PostFilter.all do
        Expect.equal (Route.parse (Route.path (Posts filter))) (Posts filter) $"The posts with {filter}"

      for filter in SubscriptionFilter.all do
        Expect.equal
          (Route.parse (Route.path (Subscriptions filter)))
          (Subscriptions filter)
          $"The subscriptions with {filter}"
    }

    test "the path of All has no query string" {
      Expect.equal (Route.path (Posts PostFilter.All)) "/posts" "The posts"
      Expect.equal (Route.path (Subscriptions SubscriptionFilter.All)) "/subscriptions" "The subscriptions"
    }

    test "an unknown filter gives no page, and All has no filter value" {
      for path in [ "/posts?filter=draft"; "/posts?filter=all"; "/posts?filter="; "/subscriptions?filter=all" ] do
        Expect.equal (Route.parse path) (NotFound path) path
    }

    test "an unknown query key does not change the route" {
      Expect.equal (Route.parse "/posts?source=mail") (Posts PostFilter.All) "No filter"
      Expect.equal (Route.parse "/posts?source=mail&filter=drafts") (Posts PostFilter.Drafts) "The filter"
      Expect.equal (Route.parse "/series?filter=drafts") Series "A page with no filter"
    }

    test "an unknown path keeps the path" {
      for path in [ "/nothing"; "/posts/hello/comments/extra"; "/settings/more" ] do
        Expect.equal (Route.parse path) (NotFound path) path
        Expect.equal (Route.path (NotFound path)) path path
    }
  ]
