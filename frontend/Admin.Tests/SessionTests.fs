module TwoPoint.Admin.Tests.SessionTests

open TwoPoint.Admin

open Expecto

let private active: Session.Model<string> = { Status = Session.Active; Waiting = [] }

let private run (msgs: Session.Msg<string> list) (model: Session.Model<string>) =
  msgs |> List.fold (fun (model, _) msg -> Session.update msg model) (model, None)

[<Tests>]
let tests =
  testList "Session" [
    test "the session starts with no banner, and calls nothing until the start completes" {
      let model, effect = Session.init ()
      Expect.equal model.Status Session.Starting "The admin reads the session"
      Expect.equal effect (Some Session.Start) "The start effect"
      Expect.isFalse (Session.isActive model) "No API call goes out"
      Expect.isFalse (Session.showsBanner model) "No banner"
    }

    test "a start with a session makes the session active" {
      let model, effect = Session.update (Session.Started true) (fst (Session.init ()))
      Expect.isTrue (Session.isActive model) "Active"
      Expect.isNone effect "No sign-in"
    }

    test "a start with no session sends the page to the Entra sign-in" {
      let model, effect = Session.update (Session.Started false) (fst (Session.init ()))
      Expect.equal model.Status Session.Redirecting "The page leaves"
      Expect.equal effect (Some Session.SignInByRedirect) "The redirect sign-in"
    }

    test "a failed redirect gives a Sign in button that tries the redirect again" {
      let model, _ = run [ Session.Started false; Session.SignInFailed ] (fst (Session.init ()))
      Expect.equal model.Status Session.Broken "The sign-in did not complete"
      let model, effect = Session.update Session.SignInClicked model
      Expect.equal model.Status Session.Redirecting "The page leaves again"
      Expect.equal effect (Some Session.SignInByRedirect) "The redirect sign-in"
    }

    test "an interaction error shows the banner and keeps the failed call" {
      let model, effect = Session.update (Session.InteractionRequired "load") active
      Expect.isTrue (Session.showsBanner model) "The banner shows"
      Expect.equal (Session.bannerText model) "Your session ended. Sign in again to continue." "The banner text"
      Expect.equal model.Waiting [ "load" ] "The failed call waits"
      Expect.isNone effect "No popup before a click"
    }

    test "Sign in again opens the popup, and the banner stays while it is open" {
      let model, effect = run [ Session.InteractionRequired "load"; Session.SignInAgainClicked ] active
      Expect.equal model.Status Session.Renewing "The popup is open"
      Expect.equal effect (Some Session.SignInByPopup) "The popup sign-in"
      Expect.isTrue (Session.showsBanner model) "The banner shows"
    }

    test "after the popup, each failed call runs again and the banner goes" {
      let model, effect =
        run
          [ Session.InteractionRequired "load"
            Session.InteractionRequired "save"
            Session.InteractionRequired "load"
            Session.SignInAgainClicked
            Session.Renewed ]
          active

      Expect.equal effect (Some(Session.Retry [ "load"; "save" ])) "Each failed call one time, in order"
      Expect.isTrue (Session.isActive model) "Active"
      Expect.isEmpty model.Waiting "No call waits"
      Expect.isFalse (Session.showsBanner model) "The banner goes"
    }

    test "a call that fails while the popup is open also runs again after the popup" {
      let _, effect =
        run
          [ Session.InteractionRequired "load"
            Session.SignInAgainClicked
            Session.InteractionRequired "save"
            Session.Renewed ]
          active

      Expect.equal effect (Some(Session.Retry [ "load"; "save" ])) "Both calls"
    }

    test "a failed popup keeps the banner and the failed call, and tells the author to try again" {
      let model, effect =
        run [ Session.InteractionRequired "load"; Session.SignInAgainClicked; Session.RenewFailed ] active

      Expect.equal model.Status (Session.Expired true) "The banner stays"
      Expect.equal (Session.bannerText model) "The sign-in did not complete. Try again." "The banner text"
      Expect.equal model.Waiting [ "load" ] "The call still waits"
      Expect.isNone effect "No retry"
    }

    test "a second click while the popup is open opens no second popup" {
      let _, effect = run [ Session.InteractionRequired "load"; Session.SignInAgainClicked; Session.SignInAgainClicked ] active
      Expect.isNone effect "One popup"
    }

    test "a refused account shows the banner, and its button sends the page to the Entra sign-in" {
      let model, effect = Session.update Session.AccountRefused active
      Expect.equal model.Status Session.Refused "The API refuses the account"
      Expect.isTrue (Session.showsBanner model) "The banner shows"
      Expect.equal (Session.bannerText model) "The API refused this account. Sign in with the author account." "The banner text"
      Expect.isNone effect "No sign-in before a click"
      let model, effect = Session.update Session.SignInClicked model
      Expect.equal model.Status Session.Redirecting "The page leaves"
      Expect.equal effect (Some Session.SignInByRedirect) "The redirect sign-in, where the author chooses the account"
    }

    test "Sign out sends the page to the Entra sign-out, and no API call goes out" {
      let model, effect = Session.update Session.SignOutClicked active
      Expect.equal model.Status (Session.SigningOut Session.Active) "The page leaves"
      Expect.equal effect (Some Session.SignOut) "The sign-out"
      Expect.isFalse (Session.isActive model) "No API call goes out"
      Expect.isFalse (Session.showsBanner model) "No banner"
    }

    test "a failed sign-out gives back the status from before" {
      let expired, _ = Session.update (Session.InteractionRequired "load") active
      let model, effect = run [ Session.SignOutClicked; Session.SignOutFailed ] expired
      Expect.equal model expired "The banner and the failed call are back"
      Expect.isNone effect "No retry"
    }

    test "Sign out does nothing while the page leaves for Entra" {
      let redirecting, _ = Session.update (Session.Started false) (fst (Session.init ()))

      for model in [ fst (Session.init ()); redirecting; fst (Session.update Session.SignOutClicked active) ] do
        let next, effect = Session.update Session.SignOutClicked model
        Expect.equal next model $"{model.Status} does not change"
        Expect.isNone effect $"{model.Status} sends no sign-out"
    }
  ]
