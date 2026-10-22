module TwoPoint.Admin.Tests.RouterTests

open TwoPoint.Admin

open Expecto

[<Tests>]
let tests =
  testList "Router" [
    test "the first route comes from the path of the page" {
      Expect.equal (Router.init "/posts/hello") { Route = Post "hello" } "The editor of a post"
      Expect.equal (Router.init "/") { Route = Dashboard } "The dashboard"
    }

    test "a followed link shows its route and pushes its path" {
      let model, effect = Router.update (Router.LinkFollowed "/comments") (Router.init "/")
      Expect.equal model { Route = ModerationQueue } "The moderation queue"
      Expect.equal effect (Some(Router.PushUrl "/comments")) "The URL changes"
    }

    test "a followed link pushes the path of its route" {
      let _, effect = Router.update (Router.LinkFollowed "/posts/") (Router.init "/")
      Expect.equal effect (Some(Router.PushUrl "/posts")) "The path with no slash at the end"
    }

    test "a followed link to the current route pushes nothing" {
      let start = Router.init "/series"
      let model, effect = Router.update (Router.LinkFollowed "/series") start
      Expect.equal model start "The model does not change"
      Expect.isNone effect "No new history entry"
    }

    test "a URL change from the browser shows its route and pushes nothing" {
      let model, effect = Router.update (Router.UrlChanged "/subscriptions") (Router.init "/settings")
      Expect.equal model { Route = Subscriptions } "The subscriptions"
      Expect.isNone effect "The browser already changed the URL"
    }

    test "the navigation lists each page in order" {
      Expect.equal
        (Router.navigation |> List.map Router.title)
        [ "Dashboard"; "Posts"; "Moderation queue"; "Commenters"; "Series"; "Subscriptions"; "Settings" ]
        "The navigation"
    }

    test "each page except the editor of a post shows the navigation" {
      for route in Router.navigation @ [ NotFound "/nothing" ] do
        Expect.isTrue (Router.showsNavigation route) $"{route} shows the navigation"

      Expect.isFalse (Router.showsNavigation (Post "hello")) "The editor shows no navigation"
    }

    test "the navigation marks the page of the route, and Posts for the editor of a post" {
      let current route =
        Router.navigation |> List.filter (fun item -> Router.isCurrent item route)

      Expect.equal (current Commenters) [ Commenters ] "The commenters"
      Expect.equal (current (Post "hello")) [ Posts ] "The posts"
      Expect.equal (current (NotFound "/nothing")) [] "No page"
    }
  ]
