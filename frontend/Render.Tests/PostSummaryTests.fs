module TwoPoint.Site.Render.Tests.PostSummaryTests

open TwoPoint.Site.Render

open Expecto
open System

let private nothing = { Title = None; PublishDate = None }

[<Tests>]
let tests =
  testList "Post summary" [
    test "a post gives its title and its publish date" {
      let summary = Post.summary "---\ntitle: A post\npublishDate: 2026-02-04T13:00:00Z\n---\n\nBody."
      Expect.equal summary.Title (Some "A post") "The title"
      Expect.equal summary.PublishDate (Some(DateTimeOffset(2026, 2, 4, 13, 0, 0, TimeSpan.Zero))) "The date"
    }

    test "a draft with a title only gives no publish date" {
      Expect.equal (Post.summary "---\ntitle: A draft\n---\n\nBody.") { nothing with Title = Some "A draft" } "Only the title"
    }

    test "a field that is wrong or missing gives nothing, and the other fields stay" {
      Expect.equal
        (Post.summary "---\ntitle: A post\npublishDate: soon\n---\n")
        { nothing with Title = Some "A post" }
        "A wrong date"

      Expect.equal (Post.summary "---\nauthor: Matt\n---\n") nothing "No title"
    }

    test "text with no front matter, or broken front matter, gives nothing" {
      Expect.equal (Post.summary "Body only.") nothing "No front matter"
      Expect.equal (Post.summary "---\ntitle: [\n---\n") nothing "Broken YAML"
      Expect.equal (Post.summary "") nothing "Empty"
    }
  ]
