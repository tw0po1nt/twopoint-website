module TwoPoint.Site.Widgets.Tests.StopReplyEmailsTests

open TwoPoint.Api
open TwoPoint.Api.ReplyEmails
open TwoPoint.Site.Widgets

open Expecto

let private token = "4e3d2c1b-0a9f-4e8d-8c7b-6a5f4e3d2c1b"

let private reading = StopReplyEmails.init [ "token", token ] |> fst

let private active = StopReplyEmails.Active(token, "A post")

let private stopping = StopReplyEmails.Stopping(token, "A post")

let private answer (state: ReplyEmailsState) : ApiResponse<ReplyEmailsState> =
  { Success = true
    Message = None
    Data = Some state }

let private refusal : ApiResponse<ReplyEmailsState> =
  { Success = false
    Message = Some "The table is down"
    Data = None }

[<Tests>]
let tests =
  testList "StopReplyEmails" [
    test "a link with a token reads the state of the token one time" {
      Expect.equal
        (StopReplyEmails.init [ "utm_source", "email"; "token", $" {token} " ])
        (StopReplyEmails.Reading token, Some(StopReplyEmails.Read token))
        "The widget reads the state and sends no stop"
    }

    test "a link with no token, or an empty token, sends no request" {
      for query in [ []; [ "token", "" ]; [ "token", "   " ]; [ "other", token ] ] do
        Expect.equal (StopReplyEmails.init query) (StopReplyEmails.MissingToken, None) $"%A{query} gives no token"
    }

    test "an active token asks with the post title, and sends nothing" {
      Expect.equal
        (StopReplyEmails.update (StopReplyEmails.StateRead(answer (ReplyEmailsState.Active "A post"))) reading)
        (active, None)
        "The widget waits for the button"
    }

    test "a stopped token shows the stop" {
      Expect.equal
        (StopReplyEmails.update (StopReplyEmails.StateRead(answer ReplyEmailsState.Stopped)) reading)
        (StopReplyEmails.Stopped, None)
        "The token is stopped"
    }

    test "an unknown token shows that the link doesn't work" {
      Expect.equal
        (StopReplyEmails.update (StopReplyEmails.StateRead(answer ReplyEmailsState.Unknown)) reading)
        (StopReplyEmails.Unknown, None)
        "The token is unknown"
    }

    test "a failed read shows the failure, and offers no stop" {
      Expect.equal
        (StopReplyEmails.update (StopReplyEmails.StateRead refusal) reading)
        (StopReplyEmails.Unreadable StopReplyEmails.Refused, None)
        "The API refused"

      Expect.equal
        (StopReplyEmails.update StopReplyEmails.StateUnread reading)
        (StopReplyEmails.Unreadable StopReplyEmails.Unreachable, None)
        "The server is unreachable"

      Expect.equal
        (StopReplyEmails.update StopReplyEmails.StopSelected (StopReplyEmails.Unreadable StopReplyEmails.Refused))
        (StopReplyEmails.Unreadable StopReplyEmails.Refused, None)
        "No stop after a failed read"
    }

    test "the confirm sends the stop of the token" {
      Expect.equal (StopReplyEmails.update StopReplyEmails.StopSelected active) (stopping, Some(StopReplyEmails.Stop token)) "One stop"
      Expect.equal (StopReplyEmails.update StopReplyEmails.StopSelected stopping) (stopping, None) "No second stop while one is sent"
    }

    test "a stop that goes through shows the stop" {
      Expect.equal
        (StopReplyEmails.update (StopReplyEmails.StopAnswered(answer ReplyEmailsState.Stopped)) stopping)
        (StopReplyEmails.Stopped, None)
        "The token is stopped"
    }

    test "a stop of a token that is gone shows that the link doesn't work" {
      Expect.equal
        (StopReplyEmails.update (StopReplyEmails.StopAnswered(answer ReplyEmailsState.Unknown)) stopping)
        (StopReplyEmails.Unknown, None)
        "The token is unknown"
    }

    test "a failed stop keeps the question, and the confirm can send the stop again" {
      let failed = StopReplyEmails.StopFailed(token, "A post", StopReplyEmails.Refused)

      Expect.equal (StopReplyEmails.update (StopReplyEmails.StopAnswered refusal) stopping) (failed, None) "The API refused"

      Expect.equal
        (StopReplyEmails.update (StopReplyEmails.StopAnswered(answer (ReplyEmailsState.Active "A post"))) stopping)
        (failed, None)
        "A stop that leaves the token active failed"

      Expect.equal
        (StopReplyEmails.update StopReplyEmails.StopUnanswered stopping)
        (StopReplyEmails.StopFailed(token, "A post", StopReplyEmails.Unreachable), None)
        "The server is unreachable"

      Expect.equal (StopReplyEmails.update StopReplyEmails.StopSelected failed) (stopping, Some(StopReplyEmails.Stop token)) "A second try"
    }

    test "an answer that comes at the wrong time changes nothing" {
      for model in [ StopReplyEmails.MissingToken; active; StopReplyEmails.Stopped; StopReplyEmails.Unknown ] do
        Expect.equal (StopReplyEmails.update (StopReplyEmails.StateRead(answer ReplyEmailsState.Stopped)) model) (model, None) $"%A{model} stays"
        Expect.equal (StopReplyEmails.update (StopReplyEmails.StopAnswered(answer ReplyEmailsState.Stopped)) model) (model, None) $"%A{model} stays"
    }
  ]
