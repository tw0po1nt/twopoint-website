module TwoPoint.Admin.Tests.ShellTests

open TwoPoint.Admin

open Expecto

let private counts: Api.DashboardCounts =
  { Drafts = 1
    PendingEdits = 2
    DeliveriesToStart = 3
    ActiveSubscriptions = 40
    NewSubscriptions = 5
    CanceledSubscriptions = 6 }

[<Tests>]
let tests =
  testList "Shell" [
    test "the shell starts the sign-in and sends no API call" {
      let model, effect = Shell.init "/"
      Expect.equal effect (Some(Shell.SignIn Session.Start)) "Only the start"
      Expect.equal model.Dashboard Dashboard.Waiting "The dashboard waits"
    }

    test "a started session loads the moderation queue" {
      let model, _ = Shell.init "/comments"
      let _, effect = Shell.update (Shell.Session(Session.Started true)) model
      Expect.equal effect (Some(Shell.Send [ Shell.LoadQueue ])) "The first API calls"
    }

    test "a started session on the dashboard loads the counts of the dashboard" {
      let model, _ = Shell.init "/"
      let _, effect = Shell.update (Shell.Session(Session.Started true)) model
      Expect.equal effect (Some(Shell.Send [ Shell.LoadQueue; Shell.LoadDashboard; Shell.LoadSiteDeploy ])) "The first API calls"
    }

    test "each visit to the dashboard loads the counts again" {
      let model, _ = Shell.init "/"
      let model, _ = Shell.update (Shell.Session(Session.Started true)) model
      let model, _ = Shell.update (Shell.Dashboard(Dashboard.Answered(Ok counts))) model
      let model, _ = Shell.update (Shell.Router(Router.LinkFollowed "/posts")) model
      let model, effect = Shell.update (Shell.Router(Router.LinkFollowed "/")) model
      Expect.equal model.Dashboard Dashboard.Waiting "The counts wait"
      Expect.equal effect (Some(Shell.Navigate(Router.PushUrl "/", [ Shell.LoadDashboard; Shell.LoadSiteDeploy ]))) "The counts load"
    }

    test "an open request from the push worker keeps the fragment of the comment in the route" {
      let model, _ = Shell.init "/"
      let model, effect = Shell.update (Shell.OpenRequested "/posts/first/comments#c-1") model
      Expect.equal model.Router { Route = PostComments "first"; Fragment = "c-1" } "The route has the comment"
      Expect.isSome effect "The page loads the comments"
    }

    test "a loaded moderation queue sets the badge to its count" {
      let model, _ = Shell.init "/comments"
      let model, _ = Shell.update (Shell.Session(Session.Started true)) model
      let _, effect = Shell.update (Shell.Queue(Queue.Answered(Ok []))) model
      Expect.equal effect (Some(Shell.SetBadge 0)) "The badge clears at 0"
    }

    test "a start with no session sends no API call" {
      let model, _ = Shell.init "/"
      let _, effect = Shell.update (Shell.Session(Session.Started false)) model
      Expect.equal effect (Some(Shell.SignIn Session.SignInByRedirect)) "The redirect sign-in"
    }

    test "the session after the popup sends the failed call again" {
      let model, _ = Shell.init "/"

      let model, effect =
        [ Session.Started true
          Session.InteractionRequired Shell.LoadDashboard
          Session.SignInAgainClicked
          Session.Renewed ]
        |> List.fold (fun (model, _) msg -> Shell.update (Shell.Session msg) model) (model, None)

      Expect.equal effect (Some(Shell.SignIn(Session.Retry [ Shell.LoadDashboard ]))) "The dashboard call again"
      Expect.isTrue (Session.isActive model.Session) "Active"
    }

    test "the dashboard shows the counts of the API" {
      let model, _ = Shell.init "/"
      let model, _ = Shell.update (Shell.Dashboard(Dashboard.Answered(Ok counts))) model
      Expect.equal model.Dashboard (Dashboard.Loaded counts) "The counts"
    }

    test "the to-do counts link to the moderation queue and to the filters of the post list" {
      let links = Dashboard.toDo (Some 7) counts

      Expect.equal
        links
        [ { Dashboard.Title = "Moderation queue"
            Count = Some 7
            Route = ModerationQueue }
          { Dashboard.Title = "Drafts"
            Count = Some 1
            Route = Posts PostFilter.Drafts }
          { Dashboard.Title = "Pending edits"
            Count = Some 2
            Route = Posts PostFilter.PendingEdits }
          { Dashboard.Title = "Pending deliveries"
            Count = Some 3
            Route = Posts PostFilter.PendingDeliveries } ]
        "The rows"
    }

    test "the audience shows the new and the canceled subscriptions of the last 30 days" {
      Expect.equal (Dashboard.audienceText counts) "+5 new, −6 canceled in the last 30 days" "The text"
    }

    test "the audience text shows a sign only on a count that is more than zero" {
      let counts =
        { counts with
            NewSubscriptions = 4
            CanceledSubscriptions = 0 }

      Expect.equal (Dashboard.audienceText counts) "+4 new, 0 canceled in the last 30 days" "No sign on zero canceled"

      Expect.equal
        (Dashboard.audienceText { counts with NewSubscriptions = 0 })
        "0 new, 0 canceled in the last 30 days"
        "No sign on zero new"
    }

    test "a route with a filter in its query string opens the page with that filter" {
      let model, _ = Shell.init "/"
      let model, _ = Shell.update (Shell.Session(Session.Started true)) model
      let model, effect = Shell.update (Shell.Router(Router.LinkFollowed "/posts?filter=pending-edits")) model
      Expect.equal model.Router.Route (Posts PostFilter.PendingEdits) "The post list with the filter"

      Expect.equal
        effect
        (Some(Shell.Navigate(Router.PushUrl "/posts?filter=pending-edits", [ Shell.LoadPosts ])))
        "The posts load"
    }

    test "a start at a route with a filter opens the page with that filter" {
      let model, _ = Shell.init "/subscriptions?filter=inactive"
      let _, effect = Shell.update (Shell.Session(Session.Started true)) model
      Expect.equal model.Router.Route (Subscriptions SubscriptionFilter.Inactive) "The inactive subscriptions"
      Expect.equal effect (Some(Shell.Send [ Shell.LoadQueue; Shell.LoadSubscriptions ])) "The list loads"
    }

    test "a refused token shows the reason on the dashboard" {
      let model, _ = Shell.init "/"
      let model, _ = Shell.update (Shell.Dashboard(Dashboard.Answered(Error Api.Refused))) model
      Expect.equal model.Dashboard (Dashboard.NotLoaded "Your session isn't authorized. Try signing in again.") "The reason"
    }

    test "a refused token offers another account" {
      let model, _ = Shell.init "/"
      let model, _ = Shell.update (Shell.Session(Session.Started true)) model
      let model, effect = Shell.update (Shell.Dashboard(Dashboard.Answered(Error Api.Refused))) model
      Expect.equal model.Session.Status Session.Refused "The banner offers another account"
      Expect.isNone effect "The page stays until the author chooses"
    }

    test "the shell starts with no sheet and no dialog" {
      let model, _ = Shell.init "/"
      Expect.isNone model.Overlay "Nothing over the page"
    }

    test "a link in the More sheet closes the sheet" {
      let model, _ = Shell.init "/"
      let model, _ = Shell.update (Shell.Opened Shell.MoreSheet) model
      Expect.equal model.Overlay (Some Shell.MoreSheet) "The sheet is open"
      let model, effect = Shell.update (Shell.Router(Router.LinkFollowed "/series")) model
      Expect.isNone model.Overlay "The sheet closes"
      Expect.equal effect (Some(Shell.Navigate(Router.PushUrl "/series", []))) "The page changes"
    }

    test "the close of a sheet that another overlay replaced keeps the new overlay" {
      let model, _ = Shell.init "/"
      let model, _ = Shell.update (Shell.Opened Shell.MoreSheet) model
      let model, _ = Shell.update (Shell.Opened Shell.SignOutDialog) model
      let model, _ = Shell.update (Shell.Closed Shell.MoreSheet) model
      Expect.equal model.Overlay (Some Shell.SignOutDialog) "The dialog stays"
      let model, _ = Shell.update (Shell.Closed Shell.SignOutDialog) model
      Expect.isNone model.Overlay "The dialog closes"
    }

    test "Sign out in the dialog closes the dialog and sends the page to the Entra sign-out" {
      let model, _ = Shell.init "/"
      let model, _ = Shell.update (Shell.Session(Session.Started true)) model
      let model, _ = Shell.update (Shell.Opened Shell.SignOutDialog) model
      let model, effect = Shell.update (Shell.Session Session.SignOutClicked) model
      Expect.isNone model.Overlay "The dialog closes"
      Expect.equal effect (Some(Shell.SignIn Session.SignOut)) "The sign-out"
    }

    test "Sign out before the session starts keeps the dialog and sends nothing" {
      let model, _ = Shell.init "/"
      let model, _ = Shell.update (Shell.Opened Shell.SignOutDialog) model
      let model, effect = Shell.update (Shell.Session Session.SignOutClicked) model
      Expect.equal model.Overlay (Some Shell.SignOutDialog) "The dialog stays"
      Expect.isNone effect "No sign-out"
    }
  ]
