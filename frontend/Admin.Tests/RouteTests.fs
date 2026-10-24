module TwoPoint.Admin.Tests.RouteTests

open TwoPoint.Admin

open Expecto

let private routes =
  [ Dashboard, "/"
    Posts, "/posts"
    Post "fable-reminders-part-2", "/posts/fable-reminders-part-2"
    PostComments "fable-reminders-part-2", "/posts/fable-reminders-part-2/comments"
    ModerationQueue, "/comments"
    Commenters, "/commenters"
    Series, "/series"
    Subscriptions, "/subscriptions"
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
      Expect.equal (Route.parse "/posts/") Posts "The posts"
      Expect.equal (Route.parse "/posts/hello/") (Post "hello") "A post"
    }

    test "the slug of a post is decoded from the path, and encoded into it" {
      Expect.equal (Route.parse "/posts/hello%20world") (Post "hello world") "The slug is decoded"
      Expect.equal (Route.path (Post "hello world")) "/posts/hello%20world" "The slug is encoded"
      Expect.equal (Route.parse "/posts/hello%20world/comments") (PostComments "hello world") "The comments decode it"
      Expect.equal (Route.path (PostComments "hello world")) "/posts/hello%20world/comments" "The comments encode it"
    }

    test "an unknown path keeps the path" {
      for path in [ "/nothing"; "/posts/hello/comments/extra"; "/settings/more" ] do
        Expect.equal (Route.parse path) (NotFound path) path
        Expect.equal (Route.path (NotFound path)) path path
    }
  ]
