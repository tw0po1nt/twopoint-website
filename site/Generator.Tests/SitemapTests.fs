module TwoPoint.Site.Generator.Tests.SitemapTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto
open System
open System.Globalization
open System.Text.RegularExpressions
open System.Xml.Linq

let private xmlns = XNamespace.Get "http://www.sitemaps.org/schemas/sitemap/0.9"

/// 7 one-offs, so that `/posts/` has a page 2.
let private oneOffs =
  [ for day in 0..6 ->
      let date = DateTimeOffset(2026, 1, 1, 13, 0, 0, TimeSpan.Zero).AddDays(float day)
      postWith $"one-off-{day + 1}" (date.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)) "One-offs" [ "fsharp" ] None ]

let private partOne = postWith "part-1" "2024-11-01T13:00:00Z" "Series" [ "Fable" ] (Some "fable-reminders")

let private posts = partOne :: oneOffs

let private map stage = RenderMap.create stage buildTime widgets (content posts)

let private document (map: RenderMap) path = XDocument.Parse(siteText map path)

/// The URL path of each entry of `sitemap-0.xml`.
let private entries stage =
  let site = (Stage.config stage).SiteUrl.AbsoluteUri

  (document (map stage) "sitemap-0.xml").Root.Elements(xmlns + "url")
  |> Seq.map (fun url ->
    let loc = url.Element(xmlns + "loc").Value
    Expect.stringStarts loc site "Each entry has the site URL of the stage"
    "/" + loc.Substring site.Length)
  |> List.ofSeq

let private robotsMeta (html: string) =
  Regex.Match(html, """<meta name="robots" content="([^"]+)">""").Groups[1].Value

[<Tests>]
let tests =
  testList "Sitemap" [
    test "sitemap-index.xml names sitemap-0.xml" {
      for stage in Stage.all do
        let locs =
          (document (map stage) "sitemap-index.xml").Root.Elements(xmlns + "sitemap")
          |> Seq.map (fun sitemap -> sitemap.Element(xmlns + "loc").Value)
          |> List.ofSeq

        Expect.equal locs [ $"{(Stage.config stage).SiteUrl.AbsoluteUri}sitemap-0.xml" ] $"The {Stage.name stage} index"
    }

    test "the sitemap lists each indexable page" {
      Expect.equal
        (entries Stage.Production)
        [ "/"
          "/about/"
          "/category/one-offs/"
          "/category/one-offs/2/"
          "/posts/"
          "/posts/2/"
          "/posts/one-off-1/"
          "/posts/one-off-2/"
          "/posts/one-off-3/"
          "/posts/one-off-4/"
          "/posts/one-off-5/"
          "/posts/one-off-6/"
          "/posts/one-off-7/"
          "/posts/part-1/"
          "/privacy/"
          "/series/"
          "/series/fable-reminders/"
          "/subscribe/"
          "/unsubscribe/" ]
        "Each page with index, sorted by URL path"
    }

    test "the sitemap omits 404.html, /category/series/, each tag page, and each newsletter" {
      let entries = entries Stage.Production
      Expect.isFalse (entries |> List.exists (fun path -> path.Contains "404")) "No 404.html"
      Expect.isFalse (List.contains "/category/series/" entries) "No /category/series/"
      Expect.isFalse (entries |> List.exists (fun path -> path.StartsWith "/tag/")) "No tag page"
      Expect.isFalse (entries |> List.exists (fun path -> path.Contains "newsletter")) "No newsletter"
    }

    test "each stage lists the same pages" {
      Expect.equal (entries Stage.Staging) (entries Stage.Production) "Staging"
      Expect.equal (entries Stage.Local) (entries Stage.Production) "Local"
    }

    test "the sitemap lists a page only when its production robots meta is index,follow" {
      let map = map Stage.Production
      let entries = set (entries Stage.Production)

      for path in pagePaths map |> Set.filter (fun path -> path.EndsWith ".html") do
        let urlPath = "/" + (if path.EndsWith "index.html" then path.Substring(0, path.Length - "index.html".Length) else path)
        let indexed = robotsMeta (siteText map path) = "index,follow"
        Expect.equal (entries.Contains urlPath) indexed $"{path} is in the sitemap only when it has index,follow"
    }
  ]
