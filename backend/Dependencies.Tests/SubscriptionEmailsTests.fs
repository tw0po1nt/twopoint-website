module TwoPoint.Dependencies.Tests.SubscriptionEmailsTests

open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Dependencies.Subscriptions

open Expecto

open System.Text.RegularExpressions

let private ok result =
  match result with
  | Ok value -> value
  | Error error -> failwithf "Expected Ok, got Error %A" error

let private text value = NonEmptyString.create None value |> ok |> Some

let private paragraphs (html : string) =
  Regex.Matches(html, "<p[^>]*>(.*?)</p>", RegexOptions.Singleline) |> Seq.map _.Groups[1].Value |> List.ofSeq

[<Tests>]
let tests =
  testList "SubscriptionEmails" [
    test "the welcome intro says that new posts follow" {
      Expect.stringContains
        (SubscriptionEmails.welcomeIntro None)
        "To get you started, here is my latest post. You'll be notified of new posts as soon as they are published."
        "Expected the new sentence"
    }

    test "the subject of a new-post delivery is the post title" {
      let post = Post.create "a-post" "A post & more" "2026-02-01T00:00:00.0000000Z" PostCommentStats.Zero |> ok
      Expect.equal (SubscriptionEmails.newPostSubject post) "A post & more" "Expected the post title"
    }

    test "a new-post intro with two paragraphs gives two encoded paragraphs in the panel" {
      let html = SubscriptionEmails.newPostIntro (text "First <b>one</b> & more.\n\nSecond\none.")
      Expect.stringContains html "<div class=\"tp-px\"" "Expected the panel"
      Expect.equal
        (paragraphs html)
        [ "First &lt;b&gt;one&lt;/b&gt; &amp; more."; "Second\none." ]
        "Expected two encoded paragraphs"
    }

    test "a blank line with spaces and Windows line ends also starts a paragraph" {
      Expect.equal
        (paragraphs (SubscriptionEmails.newPostIntro (text "One\r\n  \r\nTwo")))
        [ "One"; "Two" ]
        "Expected two paragraphs"
    }

    test "no intro text gives an empty slot" {
      Expect.equal (SubscriptionEmails.newPostIntro None) "" "Expected an empty intro"
    }
  ]
