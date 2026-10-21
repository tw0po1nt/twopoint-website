module TwoPoint.Site.Generator.Tests.Fixture

open TwoPoint.Site.Generator

open Expecto
open SkiaSharp
open System
open System.Security.Cryptography
open System.Text

let jpeg (width: int) (height: int) (color: SKColor) =
  use bitmap = new SKBitmap(width, height)
  bitmap.Erase color
  use image = SKImage.FromBitmap bitmap
  use data = image.Encode(SKEncodedImageFormat.Jpeg, 90)
  data.ToArray()

let fetchWith (get: Uri -> Result<byte array, string>) =
  { new IImageFetch with
      member _.Get url = get url }

/// Each URL gives a JPEG of 1600×900, in a color that comes from the URL.
let anyImage =
  fetchWith (fun url ->
    let hash = SHA256.HashData(Encoding.UTF8.GetBytes url.AbsoluteUri)
    Ok(jpeg 1600 900 (SKColor(hash[0], hash[1], hash[2]))))

let anyImages = Images.ofFetch anyImage

let recording (fetch: IImageFetch) =
  let calls = ResizeArray<string>()

  fetchWith (fun url ->
    calls.Add url.AbsoluteUri
    fetch.Get url),
  calls

let ok result =
  match result with
  | Ok value -> value
  | Error errors -> failtest $"%A{errors}"

/// Lines 2 to 6 of a fixture post.
let frontMatter =
  "publishDate: 2026-02-04T13:00:00Z\n\
   author: Matthew Watt\n\
   title: A post\n\
   excerpt: An excerpt\n\
   category: One-offs"

/// The body starts on line 9.
let text (frontMatter: string) (body: string) = $"---\n{frontMatter}\n---\n\n{body}\n"

let file slug = $"frontend/content/posts/{slug}.md"

let seriesRecord slug : Series =
  { Slug = slug
    Title = slug
    Excerpt = "A series"
    Image = None }

let fableReminders: Series =
  { Slug = "fable-reminders"
    Title = "Fable Reminders"
    Excerpt = "Notes on Fable"
    Image = None }

let parseIn (series: Series list) slug text = Post.parse anyImages series (file slug) text

let parse slug text = parseIn [] slug text

let publishedIn (series: Series list) slug text =
  match parseIn series slug text with
  | Ok post -> post
  | Error errors -> failtest $"The fixture post has errors: %A{errors}"

let published slug text = publishedIn [] slug text

let errorsIn (series: Series list) slug text =
  match parseIn series slug text with
  | Error errors -> errors
  | Ok post -> failtest $"The fixture post has no error: %A{post}"

let errors slug text = errorsIn [] slug text

let errorAt slug line message = PostError.at (file slug) line message

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
    "pdf": { "file": "pdf-TEST.js", "imports": [ "chunk-TEST.js" ] },
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
      "pdf-TEST.js", Encoding.UTF8.GetBytes "import\"./chunk-TEST.js\";"
      "pdf.worker-TEST.js", Encoding.UTF8.GetBytes "export{};"
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

let realContent =
  lazy
    (match Site.loadContent anyImages realRoot.Value with
     | Ok content -> content
     | Error errors -> failtest (String.concat "\n" errors))

let realPosts = lazy realContent.Value.Posts

let contentWith (fetch: IImageFetch) (posts: Post list) =
  let assets = realContent.Value.Assets

  { Posts = posts
    Privacy = realContent.Value.Privacy
    Assets = assets
    OgImages = OgImages.create (Images.ofFetch fetch) assets.DefaultImage posts |> ok
    Email = realContent.Value.Email }

let content (posts: Post list) = contentWith anyImage posts

let pagePaths (map: RenderMap) =
  RenderMap.sitePaths map |> Set.filter (fun path -> not (path.StartsWith "_/"))

/// A post with the title `slug` and a body of one paragraph.
let postWith slug (publishDate: string) category (tags: string list) (series: string option) =
  let tagLines =
    match tags with
    | [] -> ""
    | tags -> "\ntags:" + (tags |> List.map (fun tag -> $"\n  - {tag}") |> String.concat "")

  let seriesLine =
    match series with
    | Some series -> $"\nseries: {series}"
    | None -> ""

  publishedIn
    (series |> Option.map seriesRecord |> Option.toList)
    slug
    (text
      $"publishDate: {publishDate}\nauthor: Matthew Watt\ntitle: {slug}\nexcerpt: An excerpt\ncategory: {category}{tagLines}{seriesLine}"
      "Text.")
