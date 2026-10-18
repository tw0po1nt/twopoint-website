module TwoPoint.Site.Dev.Tests.LookupTests

open TwoPoint.Site.Dev
open TwoPoint.Site.Generator

open Expecto
open System
open System.Text

let private noAssets =
  { Files = Map.empty
    Stylesheet = ""
    Favicons = { Ico = ""; Svg = ""; AppleTouch = "" }
    DefaultImage = { UrlPath = ""; Size = { Width = 0; Height = 0 } } }

let private noWidgets =
  { Manifest =
      { Entries = Map.empty
        Head = ""
        DevReload = "" }
    HeadScript = ""
    Files = Map.empty }

let private map =
  RenderMap.create
    Stage.Local
    DateTimeOffset.UnixEpoch
    noWidgets
    { Posts = []
      Assets = noAssets
      OgImages = { Files = Map.empty; ByPost = Map.empty } }

let private text (render: Render) = Encoding.UTF8.GetString(render ())

[<Tests>]
let tests =
  testList "Lookup" [
    test "robots.txt gives the Local robots text" {
      let path, render = Expect.wantSome (Lookup.find map "/robots.txt") "The map contains robots.txt"
      Expect.equal path "robots.txt" "The lookup gives the site path of the file"
      Expect.equal (text render) "User-agent: *\nDisallow:\n" "The dev server serves the Local robots text"
    }

    test "a path that is not in the site gives nothing" {
      Expect.isNone (Lookup.find map "/missing.txt") "A path with no entry gives nothing"
    }

    test "the stage marker is not a site path" {
      Expect.isNone (Lookup.find map "/stage") "The dev server serves only the site files"
    }
  ]
