module TwoPoint.Admin.Tests.ShellTests

open TwoPoint.Admin

open Expecto

let private post slug fresh : Api.Post =
  { Slug = slug
    Title = slug
    CommentStats = { New = fresh; Approved = 1; Rejected = 0 } }

[<Tests>]
let tests =
  testList "Shell" [
    test "the shell starts the sign-in and sends no API call" {
      let model, effect = Shell.init "/"
      Expect.equal effect (Some(Shell.SignIn Session.Start)) "Only the start"
      Expect.equal model.Dashboard Dashboard.Waiting "The dashboard waits"
    }

    test "a started session loads the dashboard" {
      let model, _ = Shell.init "/comments"
      let _, effect = Shell.update (Shell.Session(Session.Started true)) model
      Expect.equal effect (Some(Shell.Send Shell.LoadDashboard)) "The first API call"
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
  ]
