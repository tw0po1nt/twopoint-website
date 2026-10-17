module TwoPoint.Site.Generator.Tests.RobotsTests

open TwoPoint.Site.Generator

open Expecto
open System.Text

let private robots stage =
  RenderMap.create stage
  |> Map.find (SiteFile "robots.txt")
  |> fun render -> Encoding.UTF8.GetString(render ())

[<Tests>]
let tests =
  testList "Robots" [
    test "production robots.txt names the sitemap" {
      Expect.equal
        (robots Stage.Production)
        "User-agent: *\nDisallow:\n\nSitemap: https://twopoint.dev/sitemap-index.xml\n"
        "An indexable stage gives the Sitemap line"
    }

    test "staging robots.txt has no sitemap" {
      Expect.equal (robots Stage.Staging) "User-agent: *\nDisallow:\n" "A stage that is not indexable gives no Sitemap line"
    }

    test "local robots.txt has no sitemap" {
      Expect.equal (robots Stage.Local) "User-agent: *\nDisallow:\n" "A stage that is not indexable gives no Sitemap line"
    }
  ]
