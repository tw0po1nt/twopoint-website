module TwoPoint.Core.Tests.NewsletterTests

open TwoPoint.Core.Posts
open TwoPoint.Core.Tests.Fixtures

open Expecto
open System

[<Tests>]
let tests =
  testList "Newsletter" [
    test "create rejects HTML without the unsubscribe slot" {
      let html = slottedHtml.Replace(Newsletter.UnsubscribeUrlSlot, "https://twopoint.dev/unsubscribe/")
      Expect.isError (Newsletter.create html) "A newsletter without an unsubscribe slot must be rejected"
    }

    test "create rejects HTML without the intro slot" {
      let html = slottedHtml.Replace(Newsletter.IntroSlot, "")
      Expect.isError (Newsletter.create html) "A newsletter without an intro slot must be rejected"
    }

    test "fill replaces both slots" {
      let newsletter = Newsletter.create slottedHtml |> ok
      let unsubscribeUrl = Uri "https://twopoint.dev/unsubscribe/?subscriptionId=abc&source=email"

      let html = newsletter |> Newsletter.fill "<p>Hey there!</p>" unsubscribeUrl

      Expect.stringContains html "<p>Hey there!</p>" "The intro goes into the intro slot"
      Expect.stringContains
        html
        "href=\"https://twopoint.dev/unsubscribe/?subscriptionId=abc&amp;source=email\""
        "The unsubscribe URL goes into the href, encoded for HTML"
      Expect.isFalse (html.Contains Newsletter.IntroSlot) "No intro slot remains"
      Expect.isFalse (html.Contains Newsletter.UnsubscribeUrlSlot) "No unsubscribe slot remains"
    }

    test "fill with no intro removes the intro panel" {
      let newsletter = Newsletter.create panelHtml |> ok

      let html = newsletter |> Newsletter.fill "" (Uri "https://twopoint.dev/unsubscribe/")

      Expect.isFalse (html.Contains "~/twopoint") "The title bar of the panel is gone"
      Expect.isFalse (html.Contains Newsletter.IntroPanelStart) "The start marker is gone"
      Expect.isFalse (html.Contains Newsletter.IntroPanelEnd) "The end marker is gone"
      Expect.stringContains html "<h1>A post</h1>" "The post remains"
    }

    test "fill with an intro keeps the intro panel" {
      let newsletter = Newsletter.create panelHtml |> ok

      let html = newsletter |> Newsletter.fill "<p>Hey there!</p>" (Uri "https://twopoint.dev/unsubscribe/")

      Expect.stringContains html "~/twopoint" "The title bar of the panel remains"
      Expect.stringContains html "<p>Hey there!</p>" "The intro goes into the panel"
    }

    test "fill with no intro empties the slot of a newsletter with no panel markers" {
      let newsletter = Newsletter.create slottedHtml |> ok

      let html = newsletter |> Newsletter.fill "" (Uri "https://twopoint.dev/unsubscribe/")

      Expect.stringContains html "<td></td>" "The slot is empty"
    }
  ]
