module TwoPoint.Admin.Tests.RebuildTests

open TwoPoint.Admin

open Expecto

let private clicked model =
  Shell.update (Shell.Rebuild Rebuild.Clicked) model

let private answered answer model =
  Shell.update (Shell.Rebuild(Rebuild.Answered answer)) model

[<Tests>]
let tests =
  testList "Rebuild" [
    test "Rebuild site sends the rebuild call" {
      let model, _ = Shell.init "/"
      let model, effect = clicked model
      Expect.equal effect (Some(Shell.Send Shell.RebuildSite)) "The rebuild call"
      Expect.equal model.Rebuild Rebuild.Sending "The rebuild starts"
      Expect.isTrue (Rebuild.isSending model.Rebuild) "The button is off"
    }

    test "a second click while the rebuild starts sends nothing" {
      let model, _ = Shell.init "/"
      let model, _ = clicked model
      let model, effect = clicked model
      Expect.isNone effect "One call"
      Expect.equal model.Rebuild Rebuild.Sending "The rebuild still starts"
    }

    test "a started rebuild shows the production run" {
      let model, _ = Shell.init "/"
      let model, _ = clicked model
      let model, effect = answered (Ok(Api.Queued 42)) model
      Expect.isNone effect "No more calls"
      Expect.equal (Rebuild.text model.Rebuild) "The rebuild started. The production run is #42." "The run number"
    }

    test "a started rebuild with no run number says that the rebuild started" {
      let model, _ = Shell.init "/"
      let model, _ = clicked model
      let model, _ = answered (Ok Api.QueuedWithoutRunNumber) model
      Expect.equal
        (Rebuild.text model.Rebuild)
        "The rebuild started. GitHub did not send the number of the production run."
        "No run number"
    }

    test "the local backend logs the rebuild and sends nothing to GitHub" {
      let model, _ = Shell.init "/"
      let model, _ = clicked model
      let model, _ = answered (Ok Api.Logged) model
      Expect.equal
        (Rebuild.text model.Rebuild)
        "The local backend logged the rebuild and sent nothing to GitHub."
        "The log"
    }

    test "a failed dispatch shows the reason from the API, and Rebuild site sends the call again" {
      let model, _ = Shell.init "/"
      let model, _ = clicked model
      let reason = "The dispatch to tw0po1nt/twopoint-website-staging failed."
      let model, _ = answered (Error(Api.Failed reason)) model
      Expect.equal model.Rebuild (Rebuild.NotStarted reason) "The reason"
      let _, effect = clicked model
      Expect.equal effect (Some(Shell.Send Shell.RebuildSite)) "The rebuild call again"
    }

    test "a refused token offers another account" {
      let model, _ = Shell.init "/"
      let model, _ = Shell.update (Shell.Session(Session.Started true)) model
      let model, _ = clicked model
      let model, _ = answered (Error Api.Refused) model
      Expect.equal model.Session.Status Session.Refused "The banner offers another account"
    }
  ]
