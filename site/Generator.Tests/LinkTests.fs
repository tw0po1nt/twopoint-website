module TwoPoint.Site.Generator.Tests.LinkTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto

let private post slug body = published slug (text frontMatter body)

let private target = post "b-post" "## First step\n\nText."

let private check body =
  let posts = [ post "a-post" body; target ]
  Links.check (RenderMap.create Stage.Production posts |> RenderMap.sitePaths) posts

[<Tests>]
let tests =
  testList "Link check" [
    test "a link to a page and a heading in the output passes" {
      Expect.isEmpty (check "Read [b](/posts/b-post/) and [its step](/posts/b-post/#first-step).") "The links resolve"
    }

    test "a link to a file in the output passes" {
      Expect.isEmpty (check "Read [robots.txt](/robots.txt).") "robots.txt is in the output"
    }

    test "a link to a heading of the same post passes" {
      Expect.isEmpty (check "## Intro\n\nGo to [the intro](#intro).") "The anchor is on the same page"
    }

    test "a link to a path that is not in the output fails with its line" {
      Expect.equal
        (check "Text.\n\nRead [c](/posts/c-post/).")
        [ errorAt "a-post" 11 "The output has no page at /posts/c-post/." ]
        "The path must be in the output"
    }

    test "a link to a heading that is not on the page fails" {
      Expect.equal
        (check "Read [b](/posts/b-post/#last-step).")
        [ errorAt "a-post" 9 "The page /posts/b-post/ has no heading with the id 'last-step'." ]
        "The anchor must be on the page"
    }

    test "a link to a heading that is not on the same post fails" {
      Expect.equal
        (check "Go to [the end](#end).")
        [ errorAt "a-post" 9 "The post has no heading with the id 'end'." ]
        "The anchor must be on the same page"
    }

    test "a link with no trailing slash fails" {
      Expect.equal
        (check "Read [b](/posts/b-post#first-step).")
        [ errorAt "a-post" 9 "Add the trailing slash: /posts/b-post/" ]
        "A page link has a trailing slash"
    }

    test "a link to a post that is not Markdown yet passes with its anchor" {
      Expect.isEmpty
        (check "Read [why](/posts/why-the-fsharp-would-i-write-real-code-like-this/#explicit-rather-than-implicit).")
        "A pending post passes"
    }

    test "a link to a draft fails" {
      let draft = parse "c-post" (text $"{frontMatter}\ndraft: true" "Text.")
      Expect.equal draft (Ok Draft) "c-post is a draft"
      Expect.isNonEmpty (check "Read [c](/posts/c-post/).") "A draft is not in the output"
    }
  ]
