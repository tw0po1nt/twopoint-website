module TwoPoint.Site.Generator.Tests.DraftTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto

[<Tests>]
let tests =
  testList "Draft" [
    test "a draft is not a published post" {
      Expect.equal
        (parse "a-post" (text $"{frontMatter}\ndraft: true" "Text."))
        (Ok Draft)
        "A draft is not published"
    }

    test "the body of a draft gets no check" {
      Expect.equal
        (parse "a-post" (text $"{frontMatter}\ndraft: true" "<p class=\"wip\">[Later](/posts/missing/)</p>"))
        (Ok Draft)
        "A draft can contain work in progress"
    }

    test "the front matter of a draft gets each check" {
      Expect.isError (parse "a-post" (text $"{frontMatter}\ndraft: true\nsummary: none" "Text.")) "An unknown field fails in a draft"
    }

    test "a published post has a page, and a draft has none" {
      let post = published "a-post" (text frontMatter "Text.")
      let map = RenderMap.create Stage.Production buildTime widgets (content [ post ])
      Expect.contains (pagePaths map) "posts/a-post/index.html" "The published post has a page"
      Expect.equal (pagePaths map) (set [ "index.html"; "posts/a-post/index.html"; "robots.txt"; "subscribe/index.html"; "unsubscribe/index.html" ]) "The output has no other post page"
    }
  ]
