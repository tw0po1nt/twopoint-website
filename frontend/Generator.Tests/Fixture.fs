module TwoPoint.Site.Generator.Tests.Fixture

open TwoPoint.Site.Generator
open TwoPoint.Site.Render
open TwoPoint.Site.Render.Tests.Fixture

open Expecto
open System
open System.Text

let fableReminders: Series =
  { Slug = "fable-reminders"
    Title = "Fable Reminders"
    Excerpt = "Notes on Fable"
    Image = None }

let siteText (map: RenderMap) path =
  match map.TryFind(SiteFile path) with
  | Some render -> Encoding.UTF8.GetString(render ())
  | None -> failtest $"The output has no '{path}'."

let newsletterText (map: RenderMap) slug =
  match map.TryFind(Newsletter slug) with
  | Some render -> Encoding.UTF8.GetString(render ())
  | None -> failtest $"The output has no newsletter for '{slug}'."

let private widgetManifest =
  """{
  "entries": {
    "comments": { "file": "comments-TEST.js", "imports": [ "chunk-TEST.js" ] },
    "confirm-subscription": { "file": "confirm-subscription-TEST.js", "imports": [ "chunk-TEST.js" ] },
    "pdf": { "file": "pdf-TEST.js", "imports": [ "chunk-TEST.js" ] },
    "stop-reply-emails": { "file": "stop-reply-emails-TEST.js", "imports": [ "chunk-TEST.js" ] },
    "subscribe": { "file": "subscribe-TEST.js", "imports": [ "chunk-TEST.js" ] },
    "unsubscribe": { "file": "unsubscribe-TEST.js", "imports": [ "chunk-TEST.js" ] }
  },
  "head": "head-TEST.js",
  "devReload": "dev-reload-TEST.js"
}"""

let widgetFiles =
  Map
    [ "manifest.json", Encoding.UTF8.GetBytes widgetManifest
      "comments-TEST.js", Encoding.UTF8.GetBytes "import\"./chunk-TEST.js\";"
      "confirm-subscription-TEST.js", Encoding.UTF8.GetBytes "import\"./chunk-TEST.js\";"
      "pdf-TEST.js", Encoding.UTF8.GetBytes "import\"./chunk-TEST.js\";"
      "pdf.worker-TEST.js", Encoding.UTF8.GetBytes "export{};"
      "stop-reply-emails-TEST.js", Encoding.UTF8.GetBytes "import\"./chunk-TEST.js\";"
      "subscribe-TEST.js", Encoding.UTF8.GetBytes "import\"./chunk-TEST.js\";"
      "unsubscribe-TEST.js", Encoding.UTF8.GetBytes "import\"./chunk-TEST.js\";"
      "chunk-TEST.js", Encoding.UTF8.GetBytes "export{};"
      "head-TEST.js", Encoding.UTF8.GetBytes "(()=>{document.documentElement.dataset.fixture=\"head\"})();"
      "dev-reload-TEST.js", Encoding.UTF8.GetBytes "(()=>{})();" ]

let widgets = Widgets.create widgetFiles |> ok

let buildTime = DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero)

let realRoot =
  lazy
    (match Site.root AppContext.BaseDirectory with
     | Ok root -> root
     | Error message -> failtest message)

/// The posts and the series records of the post store at the move out of Git, in the layout of the pulled folder.
let fixtureFolder = "frontend/Generator.Tests/posts"

let realPostFiles =
  lazy
    (match PostFolder.read realRoot.Value fixtureFolder with
     | Ok files -> files
     | Error message -> failtest message)

let realContent =
  lazy
    (match Site.loadContent anyImages Stage.Production realRoot.Value realPostFiles.Value with
     | Ok content -> content
     | Error errors -> failtest (String.concat "\n" errors))

let realPosts = lazy realContent.Value.Posts

let contentWith (fetch: IImageFetch) (posts: Post list) =
  let assets = realContent.Value.Assets

  { Posts = posts
    Privacy = realContent.Value.Privacy
    Assets = assets
    OgImages = OgImages.create (Images.ofFetch fetch) assets.DefaultImage posts |> ok
    Email = realContent.Value.Email
    Warnings = [] }

let content (posts: Post list) = contentWith anyImage posts

let pagePaths (map: RenderMap) =
  RenderMap.sitePaths map |> Set.filter (fun path -> not (path.StartsWith "_/"))
