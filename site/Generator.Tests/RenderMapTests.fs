module TwoPoint.Site.Generator.Tests.RenderMapTests

open TwoPoint.Site.Generator

open Expecto
open System.Text

[<Tests>]
let tests =
  testList "RenderMap" [
    test "the stage marker contains the stage name" {
      for stage in Stage.all do
        let marker = RenderMap.create stage |> Map.find StageMarker
        Expect.equal (Encoding.UTF8.GetString(marker ())) (Stage.name stage) "The marker contains the stage name"
    }

    test "the map has no newsletter yet" {
      let newsletters =
        RenderMap.create Stage.Production
        |> Map.keys
        |> Seq.filter (function
          | Newsletter _ -> true
          | _ -> false)

      Expect.isEmpty newsletters "No post gives a newsletter yet"
    }
  ]
