module TwoPoint.Site.Widgets.Tests.UnsubscribeTests

open TwoPoint.Api
open TwoPoint.Site.Widgets

open Expecto

let private sending = Unsubscribe.init [ "subscriptionId", "sub-123" ] |> fst

let private answer success message : ApiResponse<unit> =
  { Success = success
    Message = message
    Data = None }

[<Tests>]
let tests =
  testList "Unsubscribe" [
    test "a link with a subscription ID sends the cancellation of that ID" {
      Expect.equal
        (Unsubscribe.init [ "utm_source", "email"; "subscriptionId", " sub-123 " ])
        (Unsubscribe.Sending, Some(Unsubscribe.Cancel { SubscriptionId = "sub-123" }))
        "The request has the trimmed ID"
    }

    test "a link with no subscription ID or an empty one gives the no ID result, and sends nothing" {
      for query in [ []; [ "subscriptionId", "" ]; [ "subscriptionId", "  " ]; [ "subscriptionid", "sub-123" ] ] do
        Expect.equal (Unsubscribe.init query) (Unsubscribe.NoSubscriptionId, None) $"%A{query} gives no ID"
    }

    test "an answer of success gives the success result" {
      Expect.equal
        (Unsubscribe.update (Unsubscribe.Answered(answer true None)) sending)
        (Unsubscribe.Unsubscribed, None)
        "The subscription is inactive"
    }

    test "a refused answer gives the API error with the message of the API" {
      Expect.equal
        (Unsubscribe.update (Unsubscribe.Answered(answer false (Some "The subscription ID is not valid"))) sending)
        (Unsubscribe.Refused "The subscription ID is not valid", None)
        "The API message shows"
    }

    test "a refused answer with no message gives the API error with a general message" {
      Expect.equal
        (Unsubscribe.update (Unsubscribe.Answered(answer false None)) sending)
        (Unsubscribe.Refused "The unsubscribe request did not go through. Try again later.", None)
        "A general message shows"
    }

    test "no answer gives the network error" {
      Expect.equal (Unsubscribe.update Unsubscribe.Unanswered sending) (Unsubscribe.Unreachable, None) "The server is unreachable"
    }

    test "an answer after a result does not change the result" {
      for result in [ Unsubscribe.NoSubscriptionId; Unsubscribe.Unsubscribed; Unsubscribe.Refused "x"; Unsubscribe.Unreachable ] do
        Expect.equal (Unsubscribe.update Unsubscribe.Unanswered result) (result, None) $"%A{result} stays"
        Expect.equal (Unsubscribe.update (Unsubscribe.Answered(answer true None)) result) (result, None) $"%A{result} stays"
    }
  ]
