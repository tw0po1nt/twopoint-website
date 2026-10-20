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
  "entries": {
    "comments": { "file": "comments-E.js", "imports": [ "chunk-B.js" ] },
    "pdf": { "file": "pdf-G.js", "imports": [ "chunk-B.js" ] },
    "subscribe": { "file": "subscribe-A.js", "imports": [ "chunk-B.js" ] },
    "unsubscribe": { "file": "unsubscribe-F.js", "imports": [ "chunk-B.js" ] }
  },
  "head": "head-C.js",
  "devReload": "dev-reload-D.js"
}"""

let private files =
  Map
    [ "manifest.json", bytes manifest
      "comments-E.js", bytes "export{};"
      "pdf-G.js", bytes "export{};"
      "pdf.worker-H.js", bytes "export{};"
      "subscribe-A.js", bytes "export{};"
      "unsubscribe-F.js", bytes "export{};"
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
        { Entries =
            Map
              [ "comments", { File = "comments-E.js"; Imports = [ "chunk-B.js" ] }
                "pdf", { File = "pdf-G.js"; Imports = [ "chunk-B.js" ] }
                "subscribe", { File = "subscribe-A.js"; Imports = [ "chunk-B.js" ] }
                "unsubscribe", { File = "unsubscribe-F.js"; Imports = [ "chunk-B.js" ] } ]
          Head = "head-C.js"
          DevReload = "dev-reload-D.js" }
        "The manifest gives each key"
    }

    test "a manifest with no key gives an error for each key" {
      let errors = Expect.wantError (WidgetManifest.parse "{}") "The empty manifest is not valid"

      Expect.equal
        errors
        [ "frontend/Widgets/dist/manifest.json has no 'entries' object."
          "frontend/Widgets/dist/manifest.json has no 'head' text at the top level."
          "frontend/Widgets/dist/manifest.json has no 'devReload' text at the top level." ]
        "Each missing key gives an error"
    }

    test "an entry with no file gives an error that names the entry" {
      let errors =
        Expect.wantError
          (WidgetManifest.parse """{ "entries": { "pdf": { "imports": [] } }, "head": "h.js", "devReload": "d.js" }""")
          "The entry is not valid"

      Expect.equal errors [ "frontend/Widgets/dist/manifest.json has no 'file' text in the entry 'pdf'." ] "The error names the entry"
    }

    test "a manifest that is not JSON gives an error" {
      let errors = Expect.wantError (WidgetManifest.parse "{") "The manifest is not valid"
      Expect.stringStarts errors.Head "frontend/Widgets/dist/manifest.json is not valid JSON" "The error names the manifest"
    }

    test "no manifest tells the author to run the widget build" {
      Expect.equal
        (errorsOf Map.empty)
        [ "frontend/Widgets/dist/manifest.json does not exist. Run `npm ci` and `npm run build` in frontend/Widgets." ]
        "The error gives the widget build commands"
    }

    test "a manifest with no entry for a widget of the contract gives an error" {
      let manifest = """{ "entries": {}, "head": "head-C.js", "devReload": "dev-reload-D.js" }"""

      Expect.equal
        (errorsOf (files |> Map.add "manifest.json" (bytes manifest)))
        [ "frontend/Widgets/dist/manifest.json has no entry for the widget 'subscribe'."
          "frontend/Widgets/dist/manifest.json has no entry for the widget 'comments'."
          "frontend/Widgets/dist/manifest.json has no entry for the widget 'unsubscribe'."
          "frontend/Widgets/dist/manifest.json has no entry for the widget 'pdf'." ]
        "The error names each widget"
    }

    test "an entry that is not a widget of the contract gives an error" {
      let manifest =
        """{
  "entries": {
    "comments": { "file": "comments-E.js", "imports": [] },
    "pdf": { "file": "pdf-G.js", "imports": [] },
    "subscribe": { "file": "subscribe-A.js", "imports": [] },
    "unsubscribe": { "file": "unsubscribe-F.js", "imports": [] },
    "search": { "file": "chunk-B.js", "imports": [] }
  },
  "head": "head-C.js",
  "devReload": "dev-reload-D.js"
}"""

      Expect.equal
        (errorsOf (files |> Map.add "manifest.json" (bytes manifest)))
        [ "frontend/Widgets/dist/manifest.json has the entry 'search', and the widget contract has no such widget." ]
        "The error names the entry"
    }

    test "a file that the manifest names and that does not exist gives an error" {
      Expect.equal
        (errorsOf (files |> Map.remove "chunk-B.js"))
        [ "frontend/Widgets/dist/manifest.json names chunk-B.js, and frontend/Widgets/dist/ has no such file." ]
        "The error names the file"
    }

    test "a head script that contains a closing script tag gives an error" {
      Expect.equal
        (errorsOf (files |> Map.add "head-C.js" (bytes "(()=>{let a='</SCRIPT>'})();")))
        [ "frontend/Widgets/dist/head-C.js contains '</script', so a page cannot put it inline." ]
        "The error names the head script"
    }

    test "the site gets each widget file under _/widgets/, with no manifest, head script, or dev reload script" {
      let created = Widgets.create files |> ok
      Expect.equal
        (created.Files |> Map.keys |> List.ofSeq)
        [ "_/widgets/chunk-B.js"
          "_/widgets/comments-E.js"
          "_/widgets/pdf-G.js"
          "_/widgets/pdf.worker-H.js"
          "_/widgets/subscribe-A.js"
          "_/widgets/unsubscribe-F.js" ]
        "Only the bundles go to the site"
      Expect.equal created.HeadScript "(()=>{})();" "The head script is the text of its file"
    }

    test "the dev reload script and each source map are dev files, and the site gets none of them" {
      let created =
        files
        |> Map.add "subscribe-A.js.map" (bytes "{}")
        |> Map.add "dev-reload-D.js.map" (bytes "{}")
        |> Widgets.create
        |> ok

      Expect.equal
        (created.DevFiles |> Map.keys |> List.ofSeq)
        [ "_/widgets/dev-reload-D.js"; "_/widgets/dev-reload-D.js.map"; "_/widgets/subscribe-A.js.map" ]
        "The dev server gets the dev files"

      let map = RenderMap.create Stage.Production buildTime created (content [])

      Expect.isEmpty
        [ for KeyValue(path, _) in created.DevFiles do
            if map.ContainsKey(SiteFile path) then path ]
        "The build writes no dev file"
    }

    test "the render map has each widget file" {
      let map = RenderMap.create Stage.Production buildTime (Widgets.create files |> ok) (content [])
      Expect.isTrue (map.ContainsKey(SiteFile "_/widgets/chunk-B.js")) "The map has the chunk"
      Expect.isFalse (map.ContainsKey(SiteFile "_/widgets/head-C.js")) "The map has no head script file"
    }

    test "each page head has the head script inline, after the stylesheet" {
      let post = postWith "a-post" "2026-01-01T00:00:00Z" "One-offs" [] None
      let html = siteText (RenderMap.create Stage.Production buildTime widgets (content [ post ])) "posts/a-post/index.html"
      let head = html.Substring(0, html.IndexOf "</head>")
      let script = $"<script>{widgets.HeadScript}</script>"
      Expect.stringContains head script "The head has the script inline"
      Expect.isGreaterThan (head.IndexOf script) (head.IndexOf "rel=\"stylesheet\"") "The script comes after the stylesheet"
    }
  
    test "a page with no mount loads no widget script" {
      let created = Widgets.create files |> ok
      let html = """<body><p>Text.</p><pre><code>&lt;div data-widget=&quot;subscribe&quot;&gt;</code></pre></body>"""
      Expect.equal (Widgets.scripts created html) { Preloads = []; Entries = [] } "No entry and no preload"
    }

    test "a page loads the entry of each widget that it mounts, and the chunks of the entry, one time each" {
      let created = Widgets.create files |> ok
      let mount = """<div data-widget="subscribe" data-api="https://api.example"></div>"""

      Expect.equal
        (Widgets.scripts created $"<body>{mount}<main>{mount}</main></body>")
        { Preloads = [ "/_/widgets/chunk-B.js" ]
          Entries = [ "/_/widgets/subscribe-A.js" ] }
        "One entry and one preload for two mounts"
    }

    test "the head preloads the chunks and loads the entry of the explorer mount, after the head script" {
      let post = postWith "a-post" "2026-01-01T00:00:00Z" "One-offs" [] None
      let map = RenderMap.create Stage.Production buildTime widgets (content [ post ])
      let html = siteText map "posts/a-post/index.html"
      let head = html.Substring(0, html.IndexOf "</head>")
      let preload = """<link rel="modulepreload" href="/_/widgets/chunk-TEST.js">"""
      let entry = """<script type="module" src="/_/widgets/subscribe-TEST.js"></script>"""
      Expect.stringContains head preload "The head preloads the chunk"
      Expect.stringContains head entry "The head loads the entry"
      Expect.isGreaterThan (head.IndexOf preload) (head.IndexOf $"<script>{widgets.HeadScript}</script>") "The preload comes after the head script"
      Expect.isGreaterThan (head.IndexOf entry) (head.IndexOf preload) "The entry comes after the preload"
      Expect.isTrue (map.ContainsKey(SiteFile "_/widgets/subscribe-TEST.js")) "The entry is in the output"
      Expect.isTrue (map.ContainsKey(SiteFile "_/widgets/chunk-TEST.js")) "The chunk is in the output"
    }
  ]
