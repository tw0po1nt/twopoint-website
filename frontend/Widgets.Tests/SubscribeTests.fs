module TwoPoint.Site.Widgets.Tests.SubscribeTests

open TwoPoint.Api
open TwoPoint.Site.Widgets

open Expecto

let private run (messages: Subscribe.Msg list) =
  messages
  |> List.fold (fun (model, _) msg -> Subscribe.update msg model) (Subscribe.init, None)

let private filled =
  [ Subscribe.EmailChanged " reader@example.com "; Subscribe.NameChanged "" ]

let private refused message =
  Subscribe.Answered { Success = false; Message = message; Data = None }

[<Tests>]
let tests =
  testList "Subscribe" [
    test "a new form shows no error and no result" {
      Expect.isFalse (Subscribe.showsEmailError Subscribe.init) "No email error"
      Expect.equal Subscribe.init.Status Subscribe.Editing "No result"
    }

    test "a submit with a valid email address sends the trimmed address and no empty name" {
      let model, effect = run (filled @ [ Subscribe.Submitted ])
      Expect.equal model.Status Subscribe.Sending "The form is sending"
      Expect.equal effect (Some(Subscribe.Send { EmailAddress = "reader@example.com"; Name = None })) "The request has the address"
    }

    test "a submit sends the trimmed name" {
      let _, effect = run [ Subscribe.EmailChanged "reader@example.com"; Subscribe.NameChanged " Reader "; Subscribe.Submitted ]
      Expect.equal effect (Some(Subscribe.Send { EmailAddress = "reader@example.com"; Name = Some "Reader" })) "The request has the name"
    }

    test "a submit with an email address that is not valid sends nothing and shows the error" {
      let model, effect = run [ Subscribe.EmailChanged "reader@"; Subscribe.Submitted ]
      Expect.isNone effect "No request"
      Expect.isTrue (Subscribe.showsEmailError model) "The error shows"
      Expect.equal model.Status Subscribe.Editing "The form is not sending"
    }

    test "the email error shows after the reader leaves a field that is not valid, and goes when the address is valid" {
      let typed, _ = run [ Subscribe.EmailChanged "reader@" ]
      Expect.isFalse (Subscribe.showsEmailError typed) "No error while the reader types"

      let left, _ = Subscribe.update Subscribe.EmailLeft typed
      Expect.isTrue (Subscribe.showsEmailError left) "The error shows after the reader leaves the field"

      let fixedModel, _ = Subscribe.update (Subscribe.EmailChanged "reader@example.com") left
      Expect.isFalse (Subscribe.showsEmailError fixedModel) "A valid address removes the error"
    }

    test "an empty email field that the reader leaves shows no error" {
      let model, _ = run [ Subscribe.EmailLeft ]
      Expect.isFalse (Subscribe.showsEmailError model) "No error for an empty field"
    }

    test "a second submit while the form is sending sends nothing" {
      let sending, _ = run (filled @ [ Subscribe.Submitted ])
      let model, effect = Subscribe.update Subscribe.Submitted sending
      Expect.isNone effect "No second request"
      Expect.equal model sending "The model does not change"
    }

    test "a subscription that goes through empties the form and shows the result" {
      let model, effect = run (filled @ [ Subscribe.Submitted; Subscribe.Answered { Success = true; Message = None; Data = None } ])
      Expect.isNone effect "No request"
      Expect.equal model { Subscribe.init with Status = Subscribe.Subscribed } "The form is empty"
    }

    test "a refused subscription keeps the form and shows the message of the API" {
      let model, _ = run (filled @ [ Subscribe.Submitted; refused (Some "The email address is not valid") ])
      Expect.equal model.Status (Subscribe.Failed "The email address is not valid") "The message of the API"
      Expect.equal model.Email " reader@example.com " "The form keeps the address"
    }

    test "a refused subscription with no message shows a general message" {
      let model, _ = run (filled @ [ Subscribe.Submitted; refused None ])
      Expect.equal model.Status (Subscribe.Failed "The subscription did not go through. Try again later.") "A general message"
    }

    test "a request with no answer shows that the site cannot get to the server, and the reader can send again" {
      let failed, _ = run (filled @ [ Subscribe.Submitted; Subscribe.Unanswered ])
      Expect.equal failed.Status (Subscribe.Failed "The site cannot get to the server. Try again later.") "The message"

      let _, effect = Subscribe.update Subscribe.Submitted failed
      Expect.isSome effect "A new submit sends again"
    }
  ]
