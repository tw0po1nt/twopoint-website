module TwoPoint.Dependencies.Tests.PostEmailsTests

open TwoPoint.Dependencies.Posts

open Expecto

open System

[<Tests>]
let tests =
  testList "PostEmails.verifyEmail" [
    test "the link is HTML-encoded in the href, and the email shows no text of the link" {
      let _, html = PostEmails.verifyEmail (Uri "https://twopoint.dev/posts/a-post/?commenterValidationId=1&name=x&comment=Call%20me")
      let encoded = "https://twopoint.dev/posts/a-post/?commenterValidationId=1&amp;name=x&amp;comment=Call%20me"
      Expect.stringContains html $"href=\"{encoded}\"" "Expected the encoded link in the href"
      Expect.equal (html.Split(encoded).Length - 1) 1 "Expected the link only in the href of the button"
      Expect.isFalse (html.Contains "Call me") "Expected no decoded text of the link"
    }

    test "the greeting names nobody, because the form takes any name" {
      let _, html = PostEmails.verifyEmail (Uri "https://twopoint.dev/posts/a-post/")
      Expect.stringContains html "Hey there!" "Expected the greeting with no name"
    }
  ]
