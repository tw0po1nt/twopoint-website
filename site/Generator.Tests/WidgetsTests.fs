module TwoPoint.Site.Generator.Tests.WidgetsTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto
open System.Text

let private bytes (text: string) = Encoding.UTF8.GetBytes text

let private errorsOf files =
  match Widgets.create files with
  | Error errors -> errors
  | Ok _ -> failtest "The widget files have no error."

let private manifest =
  """{
  "entries": { "subscribe": { "file": "subscribe-A.js", "imports": [ "chunk-B.js" ] } },
  "head": "head-C.js",
  "devReload": "dev-reload-D.js"
}"""

let private files =
  Map
    [ "manifest.json", bytes manifest
      "subscribe-A.js", bytes "export{};"
      "chunk-B.js", bytes "export{};"
      "head-C.js", bytes "(()=>{})();"
      "dev-reload-D.js", bytes "(()=>{})();" ]

[<Tests>]
let tests =
  testList "Widgets" [
    test "the manifest gives the entries, the head script, and the dev reload script" {
      let parsed = WidgetManifest.parse manifest |> ok

      Expect.equal
        parsed
        { Entries = Map [ "subscribe", { File = "subscribe-A.js"; Imports = [ "chunk-B.js" ] } ]
          Head = "head-C.js"
          DevReload = "dev-reload-D.js" }
        "The manifest gives each key"
    }

    test "a manifest with no key gives an error for each key" {
      let errors = Expect.wantError (WidgetManifest.parse "{}") "The empty manifest is not valid"

      Expect.equal
        errors
        [ "site/Widgets/dist/manifest.json has no 'entries' object."
          "site/Widgets/dist/manifest.json has no 'head' text at the top level."
          "site/Widgets/dist/manifest.json has no 'devReload' text at the top level." ]
        "Each missing key gives an error"
    }

    test "an entry with no file gives an error that names the entry" {
      let errors =
        Expect.wantError
          (WidgetManifest.parse """{ "entries": { "pdf": { "imports": [] } }, "head": "h.js", "devReload": "d.js" }""")
          "The entry is not valid"

      Expect.equal errors [ "site/Widgets/dist/manifest.json has no 'file' text in the entry 'pdf'." ] "The error names the entry"
    }

    test "a manifest that is not JSON gives an error" {
      let errors = Expect.wantError (WidgetManifest.parse "{") "The manifest is not valid"
      Expect.stringStarts errors.Head "site/Widgets/dist/manifest.json is not valid JSON" "The error names the manifest"
    }

    test "no manifest tells the author to run the widget build" {
      Expect.equal
        (errorsOf Map.empty)
        [ "site/Widgets/dist/manifest.json does not exist. Run `npm ci` and `npm run build` in site/Widgets." ]
        "The error gives the widget build commands"
    }

    test "a file that the manifest names and that does not exist gives an error" {
      Expect.equal
        (errorsOf (files |> Map.remove "chunk-B.js"))
        [ "site/Widgets/dist/manifest.json names chunk-B.js, and site/Widgets/dist/ has no such file." ]
        "The error names the file"
    }

    test "a head script that contains a closing script tag gives an error" {
      Expect.equal
        (errorsOf (files |> Map.add "head-C.js" (bytes "(()=>{let a='</SCRIPT>'})();")))
        [ "site/Widgets/dist/head-C.js contains '</script', so a page cannot put it inline." ]
        "The error names the head script"
    }

    test "the site gets each widget file under _/widgets/, with no manifest, head script, or dev reload script" {
      let created = Widgets.create files |> ok
      Expect.equal (created.Files |> Map.keys |> List.ofSeq) [ "_/widgets/chunk-B.js"; "_/widgets/subscribe-A.js" ] "Only the bundles go to the site"
      Expect.equal created.HeadScript "(()=>{})();" "The head script is the text of its file"
    }

    test "the render map has each widget file" {
      let map = RenderMap.create Stage.Production (Widgets.create files |> ok) (content [])
      Expect.isTrue (map.ContainsKey(SiteFile "_/widgets/chunk-B.js")) "The map has the chunk"
      Expect.isFalse (map.ContainsKey(SiteFile "_/widgets/head-C.js")) "The map has no head script file"
    }

    test "each page head has the head script inline, after the stylesheet" {
      let post = postWith "a-post" "2026-01-01T00:00:00Z" "One-offs" [] None
      let html = siteText (RenderMap.create Stage.Production widgets (content [ post ])) "posts/a-post/index.html"
      let head = html.Substring(0, html.IndexOf "</head>")
      let script = $"<script>{widgets.HeadScript}</script>"
      Expect.stringContains head script "The head has the script inline"
      Expect.isGreaterThan (head.IndexOf script) (head.IndexOf "rel=\"stylesheet\"") "The script comes after the stylesheet"
    }
  ]
