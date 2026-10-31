module TwoPoint.Admin.Tests.RouterTests

open TwoPoint.Admin

open Expecto

[<Tests>]
let tests =
  testList "Router" [
    test "the first route comes from the path of the page" {
      Expect.equal (Router.init "/posts/hello") { Route = Post "hello"; Fragment = "" } "The editor of a post"
      Expect.equal (Router.init "/") { Route = Dashboard; Fragment = "" } "The dashboard"
    }

    test "a followed link shows its route and pushes its path" {
      let model, effect = Router.update (Router.LinkFollowed "/comments") (Router.init "/")
      Expect.equal model { Route = ModerationQueue; Fragment = "" } "The moderation queue"
      Expect.equal effect (Some(Router.PushUrl "/comments")) "The URL changes"
    }

    test "a followed link keeps the fragment in the route and in the pushed URL" {
      let model, effect = Router.update (Router.LinkFollowed "/comments#c-1") (Router.init "/")
      Expect.equal model { Route = ModerationQueue; Fragment = "c-1" } "The comment is in the route"
      Expect.equal effect (Some(Router.PushUrl "/comments#c-1")) "The URL has the fragment"
    }

    test "a followed link with a query string and a fragment pushes a URL that has both" {
      let model, effect = Router.update (Router.LinkFollowed "/posts?filter=drafts#first") (Router.init "/")

      Expect.equal model { Route = Posts PostFilter.Drafts; Fragment = "first" } "The filter and the fragment"
      Expect.equal effect (Some(Router.PushUrl "/posts?filter=drafts#first")) "The URL has both"
    }

    test "a followed link to another filter of the same page pushes the new URL" {
      let model, effect = Router.update (Router.LinkFollowed "/subscriptions?filter=active") (Router.init "/subscriptions")

      Expect.equal model.Route (Subscriptions SubscriptionFilter.Active) "The active subscriptions"
      Expect.equal effect (Some(Router.PushUrl "/subscriptions?filter=active")) "A history entry"
    }

    test "the first route keeps the fragment of the page" {
      Expect.equal (Router.init "/posts/hello/comments#c-2") { Route = PostComments "hello"; Fragment = "c-2" } "The comment"
    }

    test "a followed link to the same route with another fragment pushes the new URL" {
      let start = Router.init "/comments#c-1"
      let model, effect = Router.update (Router.LinkFollowed "/comments#c-2") start
      Expect.equal model.Fragment "c-2" "The new comment"
      Expect.equal effect (Some(Router.PushUrl "/comments#c-2")) "The URL changes"
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
      Expect.equal model { Route = Subscriptions SubscriptionFilter.All; Fragment = "" } "The subscriptions"
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
      Expect.equal (current (Post "hello")) [ Posts PostFilter.All ] "The posts"
      Expect.equal (current (Posts PostFilter.Drafts)) [ Posts PostFilter.All ] "The posts with a filter"

      Expect.equal
        (current (Subscriptions SubscriptionFilter.Inactive))
        [ Subscriptions SubscriptionFilter.All ]
        "The subscriptions with a filter"

      Expect.equal (current (NotFound "/nothing")) [] "No page"
    }

    test "the tab bar of a phone has four pages and More" {
      Expect.equal
        (Router.tabs |> List.map Router.tabTitle)
        [ "Home"; "Posts"; "Queue"; "Readers"; "More" ]
        "The tab bar"
    }

    test "More has each page of the navigation that has no tab, in order" {
      let tabPages =
        Router.tabs
        |> List.choose (fun tab ->
          match tab with
          | Router.Page route -> Some route
          | Router.More -> None)

      Expect.equal Router.morePages [ Commenters; Series; Settings ] "The pages of More"

      Expect.equal
        (List.sort (tabPages @ Router.morePages))
        (List.sort Router.navigation)
        "Each page has a tab or a place in More, one time"
    }

    test "the tab bar marks the tab of the route, and More for a page in More" {
      let current route =
        Router.tabs |> List.filter (fun tab -> Router.isCurrentTab tab route)

      Expect.equal (current ModerationQueue) [ Router.Page ModerationQueue ] "The queue"
      Expect.equal (current (Post "hello")) [ Router.Page(Posts PostFilter.All) ] "The posts"
      Expect.equal (current Series) [ Router.More ] "More"
      Expect.equal (current (NotFound "/nothing")) [] "No tab"
    }
  ]
