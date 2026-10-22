module TwoPoint.Site.Generator.Tests.LinkTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture
open TwoPoint.Site.Render
open TwoPoint.Site.Render.Tests.Fixture

open Expecto

let private post slug body = published slug (text frontMatter body)

let private target = post "b-post" "## First step\n\nText."

let private check body =
  let posts = [ post "a-post" body; target ]
  Links.check (RenderMap.create Stage.Production buildTime widgets (content posts) |> RenderMap.sitePaths) (List.map Links.ofPost posts)

[<Tests>]
let tests =
  testList "Link check" [
    test "a link to a file in the output passes" {
      Expect.isEmpty (check "Read [robots.txt](/robots.txt).") "robots.txt is in the output"
    }

    test "a link to a list page passes" {
      Expect.isEmpty (check "Read [the one-offs](/category/one-offs/) and [page 1](/posts/).") "The list pages are in the output"
    }

    test "a link to a series with no published post fails" {
      Expect.equal
        (check "Read [the series](/series/a-series/).")
        [ errorAt "a-post" 9 "The output has no page at /series/a-series/." ]
        "A series with no published post has no page"
    }
  ]
