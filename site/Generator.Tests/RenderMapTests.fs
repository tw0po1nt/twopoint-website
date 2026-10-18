module TwoPoint.Site.Generator.Tests.RenderMapTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto
open System.Text

[<Tests>]
let tests =
  testList "RenderMap" [
    test "the stage marker contains the stage name" {
      for stage in Stage.all do
        let marker = RenderMap.create stage [] |> Map.find StageMarker
        Expect.equal (Encoding.UTF8.GetString(marker ())) (Stage.name stage) "The marker contains the stage name"
    }
  ]
