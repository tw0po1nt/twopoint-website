module TwoPoint.Site.Generator.Tests.PostsTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto
open System.IO
open System.Reflection
open System.Text.Json
open System.Text.RegularExpressions

type private AstroPost =
  { Slug: string
    ReadingTime: int
    Category: string
    Tags: string list
    Series: string option
    HeadingIds: string list }

/// The values of each post in the Astro output.
let private astro =
  use stream = Assembly.GetExecutingAssembly().GetManifestResourceStream "astro-posts.json"
  use document = JsonDocument.Parse(stream: Stream)

  let strings (element: JsonElement) =
    element.EnumerateArray() |> Seq.map _.GetString() |> List.ofSeq

  [ for post in document.RootElement.EnumerateArray() ->
      { Slug = post.GetProperty("slug").GetString()
        ReadingTime = post.GetProperty("readingTime").GetInt32()
        Category = post.GetProperty("category").GetString()
        Tags = strings (post.GetProperty "tags")
        Series = post.GetProperty("series").GetString() |> Option.ofObj
        HeadingIds = strings (post.GetProperty "headingIds") } ]

let private latest = "danger-lurks-in-system-linq-enumerable-or-default-functions"

let private zcon4 = "nighthawks-roadmap-for-zcash-with-aditya-bharadwaj-and-matthew-watt-zcon4"

let private slidesPost = "functional-programming-more-than-just-a-coding-style"

let private post slug =
  realPosts.Value |> List.find (fun post -> post.Slug = slug)

let private page slug =
  siteText (RenderMap.create Stage.Production buildTime widgets realContent.Value) $"posts/{slug}/index.html"

[<Tests>]
let tests =
  testList "The posts" [
    test "each post of the Astro build is Markdown" {
      Expect.equal (astro |> List.length) 10 "The Astro build has 10 posts"

      Expect.equal
        (realPosts.Value |> List.map _.Slug)
        (astro |> List.map _.Slug)
        "Each post has a Markdown copy with the same slug"
    }

    test "each series has a record, and each record has a post" {
      let records = Site.loadSeries realRoot.Value |> ok |> List.map _.Slug
      Expect.equal records [ "fable-reminders"; "matts-footgun-collection" ] "The 2 series have records"

      Expect.equal
        (realPosts.Value |> List.choose _.FrontMatter.Series |> List.map _.Slug |> List.distinct |> List.sort)
        records
        "Each series record has a published post"
    }

    test "each post page is in the output of each stage" {
      for stage in Stage.all do
        let map = RenderMap.create stage buildTime widgets realContent.Value

        for expected in astro do
          Expect.stringContains
            (siteText map $"posts/{expected.Slug}/index.html")
            "<h1>"
            $"The {Stage.name stage} output has {expected.Slug}"
    }

    test "each internal link of the posts goes to a page and a heading in the output" {
      for stage in Stage.all do
        match Site.renderMap stage buildTime widgets realContent.Value with
        | Ok _ -> ()
        | Error errors -> failtest (String.concat "\n" errors)
    }

    test "the latest post page has the title, the date, the excerpt, and the body" {
      let page = page latest
      Expect.stringContains page "<h1>Danger lurks in System.Linq.Enumerable&#39;s &quot;OrDefault&quot; functions</h1>" "The page has the title"
      Expect.stringContains page "<time datetime=\"2026-02-04T13:00:00Z\">2026-02-04</time>" "The page has the date"
      Expect.stringContains page "In this post, we&#39;ll look at how my misunderstanding" "The page has the excerpt"
      Expect.stringContains page "<h2 id=\"linq\">LINQ</h2>" "The page has the body"
    }

    test "the heading ids agree with the Astro output" {
      for expected in astro do
        let ids = [ for heading in Regex.Matches(page expected.Slug, """<h[1-6] id="([^"]+)">""") -> heading.Groups[1].Value ]
        Expect.equal ids expected.HeadingIds $"The ids of {expected.Slug} are the Astro ids, in order"
    }

    test "the reading times agree with the Astro values" {
      for expected in astro do
        Expect.equal
          (post expected.Slug).Body.ReadingTime
          expected.ReadingTime
          $"Astro gives {expected.ReadingTime} minutes for {expected.Slug}"
    }

    test "a post with no words shows no reading time, as in the Astro output" {
      Expect.isFalse ((page zcon4).Contains "<dt>read</dt>") "The video post has no reading time"
      Expect.stringContains (page latest) "<dt>read</dt><dd>11 min</dd>" "The latest post has its reading time"
    }

    test "the category, tag, and series slugs agree with the Astro values" {
      for expected in astro do
        let frontMatter = (post expected.Slug).FrontMatter
        Expect.equal frontMatter.Category.Slug expected.Category $"The category of {expected.Slug}"
        Expect.equal (frontMatter.Tags |> List.map _.Slug) expected.Tags $"The tags of {expected.Slug}"
        Expect.equal (frontMatter.Series |> Option.map _.Slug) expected.Series $"The series of {expected.Slug}"
    }

    test "the talks embed their video and their slides" {
      Expect.stringContains (page zcon4) "<iframe src=\"https://www.youtube-nocookie.com/embed/qRuKTkDvAk4\"" "The talk plays its video"

      Expect.stringContains
        (page slidesPost)
        "<a class=\"pdf-download\" href=\"https://twopointwebsite.blob.core.windows.net/assets/a4dd66d174d2f471.pdf\""
        "The talk links its slides"

      Expect.stringContains
        (page slidesPost)
        "<div data-widget=\"pdf\" data-file=\"https://twopointwebsite.blob.core.windows.net/assets/a4dd66d174d2f471.pdf\"></div>"
        "The talk has the Pdf mount"
    }

    test "only a post with a ::pdf directive loads the Pdf entry" {
      let pdfEntry = """<script type="module" src="/_/widgets/pdf-TEST.js"></script>"""
      Expect.stringContains (page slidesPost) pdfEntry "The talk with slides loads the Pdf entry"

      for post in realPosts.Value do
        if post.Slug <> slidesPost then
          Expect.isFalse ((page post.Slug).Contains "/_/widgets/pdf") $"{post.Slug} loads no Pdf code"

      for urlPath in [ "index.html"; "subscribe/index.html"; "unsubscribe/index.html" ] do
        let html = siteText (RenderMap.create Stage.Production buildTime widgets realContent.Value) urlPath
        Expect.isFalse (html.Contains "/_/widgets/pdf") $"{urlPath} loads no Pdf code and no worker"
    }
  ]
