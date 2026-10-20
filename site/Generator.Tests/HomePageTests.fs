module TwoPoint.Site.Generator.Tests.HomePageTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture
open TwoPoint.Site.Widgets

open Expecto
open System
open System.Text.RegularExpressions

let private hidden slug publishDate category =
  published
    slug
    (text
      $"publishDate: {publishDate}\nauthor: Matthew Watt\ntitle: {slug}\nexcerpt: An excerpt\ncategory: {category}\nshowOnHomepage: false"
      "Text.")

let private oldPost = postWith "old-post" "2023-12-20T14:00:00Z" "One-offs" [] None
let private partOne = postWith "part-1" "2024-11-01T13:00:00Z" "Series" [] (Some "fable-reminders")
let private partTwo = postWith "part-2" "2024-11-15T14:00:00Z" "Series" [] (Some "fable-reminders")
let private oneOff = postWith "one-off" "2025-11-03T14:00:00Z" "One-offs" [] None
let private hiddenTalk = hidden "hidden-talk" "2025-11-14T01:30:00Z" "Talks"

/// The build time of the fixture is 2026-10-18T12:00:00Z.
let private upcoming = postWith "upcoming" "2026-10-29T13:00:00Z" "Talks" [] None
let private later = postWith "later" "2026-12-01T13:00:00Z" "One-offs" [] None

let private posts = [ oldPost; partOne; partTwo; oneOff; hiddenTalk ]

let private home stage (posts: Post list) =
  siteText (RenderMap.create stage buildTime widgets (content posts)) "index.html"

let private section name (html: string) =
  let found = Regex.Match(html, $"""<section class="{name}">.*?</section>""", RegexOptions.Singleline)
  if found.Success then found.Value else failtest $"The page has no {name} section."

let private hrefs (html: string) =
  [ for found in Regex.Matches(html, """<a href="/posts/([^/]+)/">""") -> found.Groups[1].Value ]

[<Tests>]
let tests =
  testList "Home page" [
    test "/ has the site title, the canonical URL, and the Open Graph type of a website" {
      let html = home Stage.Production posts
      Expect.stringContains html "<title>TwoPoint</title>" "The title is the site title alone"
      Expect.stringContains html """<meta name="description" content="A blog about programming, mostly functional">""" "The description of the stage"
      Expect.stringContains html """<link rel="canonical" href="https://twopoint.dev/">""" "The canonical URL is the root"
      Expect.stringContains html """<meta property="og:type" content="website">""" "The Open Graph type"
      Expect.stringContains html """<meta name="robots" content="index,follow">""" "The page is indexable"
    }

    test "the latest card shows the newest post that does not set showOnHomepage: false" {
      let latest = section "latest" (home Stage.Local posts)
      Expect.equal (hrefs latest |> List.distinct) [ "one-off" ] "The newer hidden talk does not get the card"

      Expect.stringContains
        latest
        """<p class="meta"><time datetime="2025-11-03T14:00:00Z">2025-11-03</time> &#183; <a href="/category/one-offs/">category/one-offs</a> &#183; 1 min</p><h2><a href="/posts/one-off/">one-off</a></h2><p>An excerpt</p><a class="read" href="/posts/one-off/">read →</a>"""
        "The card has the date, the category, the reading time, the title, the excerpt, and the link"
    }

    test "the latest card of a series post links its series" {
      let latest = section "latest" (home Stage.Local [ partOne; partTwo ])
      Expect.stringContains latest """&#183; <a href="/series/fable-reminders/">series/fable-reminders</a> &#183;""" "The card links the series"
    }

    test "with no post that shows on the home page, the page has no latest card" {
      let html = home Stage.Local [ hiddenTalk ]
      Expect.isFalse (html.Contains """<section class="latest">""") "No card"
      Expect.stringContains (section "log" html) """<a href="/posts/hidden-talk/">""" "The log has the hidden talk"
    }

    test "the log groups the posts by year, the newest year and the newest post first, with the kind of each post" {
      let log = section "log" (home Stage.Local posts)
      let years = [ for found in Regex.Matches(log, "<h2>([^<]+)</h2>") -> found.Groups[1].Value ]
      Expect.equal years [ "2025"; "2024"; "2023" ] "The newest year first"
      Expect.equal (hrefs log) [ "hidden-talk"; "part-2"; "part-1"; "old-post" ] "The log omits the post of the latest card"

      Expect.stringContains
        log
        """<h2>2024</h2><ol><li><time datetime="2024-11-15T14:00:00Z">11-15</time><span class="kind">series</span><a href="/posts/part-2/">part-2</a></li><li>"""
        "Each line has the date, the kind, and the title"

      Expect.stringContains log """<span class="kind">talk</span><a href="/posts/hidden-talk/">""" "A talk is a talk"
      Expect.stringContains log """<span class="kind">post</span><a href="/posts/old-post/">""" "A one-off is a post"
    }

    test "the series list gives each series with its number of posts and its excerpt" {
      Expect.stringContains
        (section "series-list" (home Stage.Local posts))
        """<li><a href="/series/fable-reminders/">fable-reminders/</a><span>2 posts &#183; A series</span></li>"""
        "The series line"

      Expect.stringContains (section "series-list" (home Stage.Local [ partOne ])) "<span>1 post &#183; A series</span>" "One post"
      Expect.isFalse ((home Stage.Local [ oneOff ]).Contains "series-list") "No series gives no series list"
    }

    test "the page has a Subscribe mount with the API of the stage, and a note" {
      let subscribe = section "home-subscribe" (home Stage.Local posts)

      Expect.stringContains subscribe """<div data-widget="subscribe" data-api="http://localhost:7071">""" "The mount"

      Expect.stringContains
        subscribe
        """<p class="note">Get an email for each new post. Each email has a link to unsubscribe.</p>"""
        "The note"
    }

    test "with no post after the build time, the page has no banner" {
      Expect.isFalse ((home Stage.Local posts).Contains "upcoming") "No banner"
    }

    test "a post at the build time is not upcoming" {
      let now = postWith "now" "2026-10-18T12:00:00Z" "One-offs" [] None
      Expect.isFalse ((home Stage.Local (now :: posts)).Contains """class="upcoming""") "No banner"
    }

    test "the banner above the header shows the first post after the build time, with the time that hides it" {
      let html = home Stage.Local (later :: upcoming :: posts)

      Expect.stringContains
        html
        """<body><p class="upcoming" data-hide-at="2026-10-29T13:00:00.000Z"><span class="badge">upcoming</span><a href="/posts/upcoming/">upcoming</a><time datetime="2026-10-29T13:00:00Z">2026-10-29</time></p><header class="site-header">"""
        "The banner of the first upcoming post"

      Expect.isFalse (html.Contains """data-hide-at="2026-12-01""") "One banner"
    }

    test "the head script hides the banner from the publish date on" {
      let publishDate = DateTimeOffset(2026, 10, 29, 13, 0, 0, TimeSpan.Zero)
      let hideAt = Layout.scriptTime publishDate

      Expect.isTrue (HideAt.isPast (Layout.scriptTime publishDate) hideAt) "At the publish date"

      Expect.isFalse
        (HideAt.isPast (Layout.scriptTime (publishDate.AddMilliseconds -1.0)) hideAt)
        "One millisecond before the publish date"
    }

    test "the build includes an upcoming post in the latest card and the log" {
      let html = home Stage.Local (later :: upcoming :: posts)
      Expect.equal (hrefs (section "latest" html) |> List.distinct) [ "later" ] "The newest post gets the card"
      Expect.contains (hrefs (section "log" html)) "upcoming" "The log has the upcoming post"
    }

    test "only the home page has the banner" {
      let map = RenderMap.create Stage.Local buildTime widgets (content (upcoming :: posts))
      Expect.isFalse ((siteText map "posts/old-post/index.html").Contains "data-hide-at") "A post page has no banner"
      Expect.isFalse ((siteText map "subscribe/index.html").Contains "data-hide-at") "The Subscribe page has no banner"
    }

    test "the real home page has the latest card, the log, the series, and the Subscribe mount" {
      let html = siteText (RenderMap.create Stage.Production buildTime widgets realContent.Value) "index.html"

      for name in [ "intro"; "latest"; "log"; "series-list"; "home-subscribe" ] do
        section name html |> ignore

      Expect.equal
        (hrefs (section "latest" html) |> List.distinct)
        [ "danger-lurks-in-system-linq-enumerable-or-default-functions" ]
        "The latest card"
    }
  ]
