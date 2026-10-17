module TwoPoint.Site.Dev.Tests.LookupTests

open TwoPoint.Site.Dev
open TwoPoint.Site.Generator

open Expecto
open System.Text

let private map = RenderMap.create Stage.Local

let private text (render: Render) = Encoding.UTF8.GetString(render ())

[<Tests>]
let tests =
  testList "Lookup" [
    test "the root path gives the root index page" {
      Expect.equal (Lookup.sitePath "/") "index.html" "A folder path gives its index.html"
    }

    test "a folder path gives its index page" {
      Expect.equal (Lookup.sitePath "/posts/") "posts/index.html" "A folder path gives its index.html"
      Expect.equal (Lookup.sitePath "/posts/2/") "posts/2/index.html" "A nested folder path gives its index.html"
    }

    test "a file path gives the same file" {
      Expect.equal (Lookup.sitePath "/robots.txt") "robots.txt" "A file path gives the file"
      Expect.equal (Lookup.sitePath "/posts") "posts" "A path with no trailing slash is a file path"
    }

    test "robots.txt gives the Local robots text" {
      let render = Lookup.find map "/robots.txt"
      Expect.isSome render "The map contains robots.txt"
      Expect.equal (text render.Value) "User-agent: *\nDisallow:\n" "The dev server serves the Local robots text"
    }

    test "a path that is not in the site gives nothing" {
      Expect.isNone (Lookup.find map "/missing.txt") "A path with no entry gives nothing"
    }

    test "the stage marker is not a site path" {
      Expect.isNone (Lookup.find map "/stage") "The dev server serves only the site files"
    }
  ]
