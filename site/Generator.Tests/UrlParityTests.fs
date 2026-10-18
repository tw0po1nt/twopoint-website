module TwoPoint.Site.Generator.Tests.UrlParityTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto

[<Tests>]
let tests =
  testList "URL parity" [
    test "each pending path is on the URL list" {
      Expect.isEmpty (Set.difference UrlParity.pending UrlParity.urls) "The pending list contains only listed paths"
    }

    test "the output of each stage has each listed path that is not pending" {
      for stage in Stage.all do
        let output = RenderMap.create stage realContent.Value |> RenderMap.sitePaths
        Expect.isEmpty (UrlParity.check output) $"The {Stage.name stage} output agrees"
    }

    test "the check fails when robots.txt is missing" {
      let output = RenderMap.create Stage.Production realContent.Value |> RenderMap.sitePaths |> Set.remove "robots.txt"

      Expect.equal
        (UrlParity.check output)
        [ "The output has no 'robots.txt'." ]
        "A missing path that is not pending fails"
    }

    test "the check fails when a pending path is in the output" {
      let output = RenderMap.create Stage.Production realContent.Value |> RenderMap.sitePaths |> Set.add "rss.xml"

      Expect.equal
        (UrlParity.check output)
        [ "The output has 'rss.xml'. Remove it from the pending list." ]
        "A pending path in the output fails"
    }
  ]
