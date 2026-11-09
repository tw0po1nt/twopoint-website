module TwoPoint.Dependencies.Tests.PostEmailsTests

open TwoPoint.Dependencies.Posts

open Expecto

open System

[<Tests>]
let tests =
  testList "PostEmails.verifyEmail" [
    test "the link is HTML-encoded in the href and in the text" {
      let _, html = PostEmails.verifyEmail (Uri "https://twopoint.dev/posts/a-post/?commenterValidationId=1&name=x")
      let encoded = "https://twopoint.dev/posts/a-post/?commenterValidationId=1&amp;name=x"
      Expect.stringContains html $"href=\"{encoded}\"" "Expected the encoded link in the href"
      Expect.stringContains html $">{encoded}</a>" "Expected the encoded link in the text"
    }

    test "the greeting names nobody, because the form takes any name" {
      let _, html = PostEmails.verifyEmail (Uri "https://twopoint.dev/posts/a-post/")
      Expect.stringContains html "Hey there!" "Expected the greeting with no name"
    }
  ]
