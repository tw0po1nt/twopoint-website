module TwoPoint.Site.Generator.Tests.PostPageTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto
open System.Text.RegularExpressions

let private oldest =
  postWith "fable-reminders-part-1" "2024-11-01T00:00:00Z" "Series" [ "FSharp"; "Fable" ] (Some "fable-reminders")

let private middle =
  published
    "fable-reminders-part-2"
    (text
      "publishDate: 2024-11-15T00:00:00Z\nauthor: Matthew Watt\ntitle: Fable Reminders - Part 2\nexcerpt: The second part\ncategory: Series\ntags:\n  - FSharp\nseries:\n  slug: fable-reminders\n  title: Fable Reminders\n  excerpt: A series"
      "## First\n\nText.\n\n### Not in the outline\n\nText.\n\n#### Detail\n\nText.")

let private newest = postWith "smatterings-of-fsharp" "2025-11-14T00:00:00Z" "Talks" [ "FSharp" ] None

let private posts = [ oldest; middle; newest ]

let private page stage (post: Post) =
  siteText (RenderMap.create stage (content posts)) $"posts/{post.Slug}/index.html"

let private robots stage =
  Regex.Match(page stage middle, """<meta name="robots" content="([^"]+)">""").Groups[1].Value

[<Tests>]
let tests =
  testList "Post page" [
    test "Local and Staging pages have noindex,nofollow" {
      Expect.equal (robots Stage.Local) "noindex,nofollow" "Local is not indexable"
      Expect.equal (robots Stage.Staging) "noindex,nofollow" "Staging is not indexable"
      Expect.equal (robots Stage.Production) "index,follow" "Production is indexable"
    }

    test "the head has the title, the canonical URL, and the Open Graph tags of the stage" {
      let html = page Stage.Staging middle
      Expect.stringContains html $"<title>Fable Reminders - Part 2 {string (char 0x2014)} TwoPoint Staging</title>" "The title has the site title"
      Expect.stringContains html """<meta name="description" content="The second part">""" "The description is the excerpt"

      let url = "https://staging.twopoint.dev/posts/fable-reminders-part-2/"
      Expect.stringContains html $"""<link rel="canonical" href="{url}">""" "The canonical URL comes from the slug"
      Expect.stringContains html $"""<meta property="og:url" content="{url}">""" "og:url is the canonical URL"
      Expect.stringContains html """<meta property="og:type" content="article">""" "The Open Graph type of a post is article"
      Expect.stringContains html """<meta property="og:site_name" content="TwoPoint (Staging)">""" "The site name comes from the stage"
      Expect.stringContains html """<link rel="alternate" type="application/rss+xml" """ "The page links the feed"
      Expect.stringContains html """<meta name="google-site-verification" """ "The page has the verification"
    }

    test "the stylesheet and the favicons are files in the output" {
      let map = RenderMap.create Stage.Production (content posts)
      let html = siteText map "posts/fable-reminders-part-2/index.html"

      for found in Regex.Matches(html, """<link rel="(?:stylesheet|icon|apple-touch-icon)"[^>]* href="([^"]+)""") do
        let href = found.Groups[1].Value
        Expect.isTrue (href.StartsWith "/_/") $"{href} is under /_/"
        Expect.contains (RenderMap.sitePaths map) (href.Substring 1) $"{href} is in the output"
    }

    test "only Production has the Pulse script, and no page has another script" {
      Expect.stringContains (page Stage.Production middle) """<script src="https://pulse.w7ech.com/cdn/s.js" defer data-site=""" "Production counts visits"

      for stage in [ Stage.Local; Stage.Staging ] do
        Expect.isFalse ((page stage middle).Contains "<script") $"{Stage.name stage} has no script"
    }

    test "the explorer is a popover that opens and closes with no script" {
      let html = page Stage.Local middle
      Expect.stringContains html """<aside id="explorer" class="explorer" popover="auto" """ "The explorer is a popover"
      Expect.stringContains html """<button class="explorer-toggle" type="button" popovertarget="explorer">""" "The header button opens it"
      Expect.stringContains html """popovertarget="explorer" popovertargetaction="hide">[close]""" "The close button closes it"
    }

    test "the explorer marks the current post and lists each year, series, and tag" {
      let html = page Stage.Local middle
      Expect.stringContains html """<li class="dir">posts/2025/</li>""" "The explorer has the 2025 folder"
      Expect.stringContains html """<li class="is-current"><time datetime="2024-11-15T00:00:00Z">11-15</time><a href="/posts/fable-reminders-part-2/" aria-current="page">""" "The current post is marked"
      Expect.stringContains html """<span class="kind">talk</span>""" "A talk shows its kind"
      Expect.stringContains html """<a href="/series/fable-reminders/">fable-reminders/</a><span class="count">2</span>""" "The series has its count"
      Expect.stringContains html """<a href="/tag/fsharp/">#F#</a>""" "The explorer shows each tag by its title"
    }

    test "a series post shows its position in the path line and the front matter" {
      let html = page Stage.Local middle
      Expect.stringContains html """<a href="/series/fable-reminders/">series/fable-reminders</a>/02</p>""" "The path line has the position"
      Expect.stringContains html """<a href="/series/fable-reminders/">fable-reminders</a> (2 of 2)</dd>""" "The front matter has the position"
    }

    test "a post with no series shows its category in the path line" {
      Expect.stringContains
        (page Stage.Local newest)
        """<a href="/category/talks/">category/talks</a>/smatterings-of-fsharp</p>"""
        "The path line has the category"
    }

    test "the older and newer cards link the posts by publish date" {
      let html = page Stage.Local middle
      Expect.stringContains html """<a class="card" href="/posts/fable-reminders-part-1/"><span>← older</span>fable-reminders-part-1</a>""" "The older card"
      Expect.stringContains html """<a class="card" href="/posts/smatterings-of-fsharp/"><span>newer →</span>smatterings-of-fsharp</a>""" "The newer card"
      Expect.stringContains (page Stage.Local newest) """<p class="card is-empty"><span>newer →</span>this is the newest post</p>""" "The newest post has no newer card"
    }

    test "the outline lists the h2 and h4 headings" {
      let html = page Stage.Local middle

      Expect.stringContains
        html
        """<ul><li class="outline-h2"><a href="#first">First</a></li><li class="outline-h4"><a href="#detail">Detail</a></li></ul>"""
        "The outline has the h2 and the h4, and no h3"
    }

    test "the related posts and the share links follow the post" {
      let html = page Stage.Production middle
      Expect.stringContains html """<ul class="related"><li><a href="/posts/fable-reminders-part-1/">fable-reminders-part-1</a></li><li><a href="/posts/smatterings-of-fsharp/">""" "The related posts by score"
      Expect.stringContains html """<a href="https://www.facebook.com/sharer.php?u=https%3A%2F%2Ftwopoint.dev%2Fposts%2Ffable-reminders-part-2%2F" target="_blank" rel="noopener noreferrer">facebook</a>""" "A share link opens in a new tab"
      Expect.stringContains html """<a href="mailto:?subject=Fable%20Reminders%20-%20Part%202&amp;body=""" "The email link stays in the tab"
      Expect.stringContains html """<details class="panel" id="comments"><summary>$ comments --list</summary></details>""" "The Comments panel is empty"
    }
  ]
