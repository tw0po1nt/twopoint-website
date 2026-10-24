module TwoPoint.Admin.Tests.ShellTests

open TwoPoint.Admin

open Expecto

let private post slug fresh : Api.Post =
  { Slug = slug
    Title = slug
    State = Api.Published
    PendingEdit = false
    PublishDate = Some "2026-01-02T03:04:05Z"
    CommentStats = { New = fresh; Approved = 1; Rejected = 0 } }

[<Tests>]
let tests =
  testList "Shell" [
    test "the shell starts the sign-in and sends no API call" {
      let model, effect = Shell.init "/"
      Expect.equal effect (Some(Shell.SignIn Session.Start)) "Only the start"
      Expect.equal model.Dashboard Dashboard.Waiting "The dashboard waits"
    }

    test "a started session loads the dashboard and the moderation queue" {
      let model, _ = Shell.init "/comments"
      let _, effect = Shell.update (Shell.Session(Session.Started true)) model
      Expect.equal effect (Some(Shell.Send [ Shell.LoadDashboard; Shell.LoadQueue ])) "The first API calls"
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

    test "the dashboard counts the posts and the new comments" {
      let model, _ = Shell.init "/"
      let answer = Ok [ post "first" 2; post "second" 1 ] |> Dashboard.Answered |> Shell.Dashboard
      let model, _ = Shell.update answer model
      Expect.equal model.Dashboard (Dashboard.Loaded { Posts = 2; NewComments = 3 }) "Two posts and three new comments"
    }

    test "a refused token shows the reason on the dashboard" {
      let model, _ = Shell.init "/"
      let model, _ = Shell.update (Shell.Dashboard(Dashboard.Answered(Error Api.Refused))) model
      Expect.equal model.Dashboard (Dashboard.NotLoaded "The API refused the token of this account.") "The reason"
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
