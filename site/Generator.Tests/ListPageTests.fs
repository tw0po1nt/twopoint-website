module TwoPoint.Site.Generator.Tests.ListPageTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto
open System
open System.Globalization
open System.Text.RegularExpressions

/// 13 one-offs, one a day from 2025-12-20, so that the log of page 1 has two years.
let private oneOffs =
  [ for day in 0..12 ->
      let date = DateTimeOffset(2025, 12, 20, 13, 0, 0, TimeSpan.Zero).AddDays(float day)
      postWith $"one-off-{day + 1:D2}" (date.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)) "One-offs" [] None ]

let private partOne = postWith "part-1" "2024-11-01T13:00:00Z" "Series" [ "fsharp"; "Fable" ] (Some "fable-reminders")
let private partTwo = postWith "part-2" "2024-11-15T14:00:00Z" "Series" [ "fsharp" ] (Some "fable-reminders")
let private talk = postWith "a-talk" "2023-06-01T13:00:00Z" "Talks" [ ".NET"; "csharp" ] None

let private map stage (posts: Post list) = RenderMap.create stage buildTime widgets (content posts)

let private page (posts: Post list) path = siteText (map Stage.Production posts) path

let private slugs (html: string) =
  [ for found in Regex.Matches(html, """<li><time datetime="[^"]+">[^<]+</time><span class="kind">[^<]+</span><a href="/posts/([^/]+)/">""") ->
      found.Groups[1].Value ]

let private pager (html: string) =
  let found = Regex.Match(html, """<nav class="pager".*?</nav>""", RegexOptions.Singleline)
  if found.Success then Some found.Value else None

let private listPaths (posts: Post list) =
  pagePaths (map Stage.Production posts)
  |> Set.filter (fun path -> [ "posts/"; "category/"; "series/"; "tag/" ] |> List.exists path.StartsWith)
  |> Set.filter (fun path -> not (Regex.IsMatch(path, "^posts/[^/0-9][^/]*/index.html$")))

let private emDash = string (char 0x2014)

[<Tests>]
let tests =
  testList "List pages" [
    test "each list has 6 posts on a page, page 1 has no number, and page 2 and later are <base>/<n>/" {
      let paths = listPaths oneOffs

      Expect.equal
        paths
        (set
          [ "posts/index.html"
            "posts/2/index.html"
            "posts/3/index.html"
            "category/one-offs/index.html"
            "category/one-offs/2/index.html"
            "category/one-offs/3/index.html"
            "series/index.html" ])
        "13 posts give 3 pages of each list"

      Expect.equal (slugs (page oneOffs "posts/index.html")) [ for day in 13..-1..8 -> $"one-off-{day:D2}" ] "Page 1 has the newest 6"
      Expect.equal (slugs (page oneOffs "posts/2/index.html")) [ for day in 7..-1..2 -> $"one-off-{day:D2}" ] "Page 2 has the next 6"
      Expect.equal (slugs (page oneOffs "posts/3/index.html")) [ "one-off-01" ] "Page 3 has the oldest"
    }

    test "the log of a page groups its posts by year, the newest year first" {
      let html = page oneOffs "posts/index.html"
      let years = [ for found in Regex.Matches(html, """<h2>(\d{4})</h2>""") -> found.Groups[1].Value ]
      Expect.equal years [ "2026"; "2025" ] "Page 1 spans two years"

      Expect.stringContains
        html
        """<h2>2025</h2><ol><li><time datetime="2025-12-31T13:00:00Z">12-31</time><span class="kind">post</span><a href="/posts/one-off-12/">one-off-12</a></li>"""
        "Each line has the date, the kind, and the title"
    }

    test "the pager links the newer and the older page" {
      let pagerOf path = pager (page oneOffs path) |> Option.defaultWith (fun () -> failtest $"{path} has no pager")

      Expect.equal
        (pagerOf "posts/index.html")
        """<nav class="pager" aria-label="Pages"><span>page 1 of 3</span><a class="older" href="/posts/2/" rel="next">older →</a></nav>"""
        "Page 1 links only the older page"

      Expect.equal
        (pagerOf "posts/2/index.html")
        """<nav class="pager" aria-label="Pages"><a class="newer" href="/posts/" rel="prev">← newer</a><span>page 2 of 3</span><a class="older" href="/posts/3/" rel="next">older →</a></nav>"""
        "Page 2 links page 1 with no number"

      Expect.equal
        (pagerOf "category/one-offs/3/index.html")
        """<nav class="pager" aria-label="Pages"><a class="newer" href="/category/one-offs/2/" rel="prev">← newer</a><span>page 3 of 3</span></nav>"""
        "The last page links only the newer page"

      Expect.isNone (pager (page [ partOne; partTwo ] "posts/index.html")) "A list of one page has no pager"
    }

    test "page 2 has its number in the title, the command, and the canonical URL" {
      let html = page oneOffs "posts/2/index.html"
      Expect.stringContains html $"<title>Posts, page 2 {emDash} TwoPoint</title>" "The title"
      Expect.stringContains html """<link rel="canonical" href="https://twopoint.dev/posts/2/">""" "The canonical URL"
      Expect.stringContains html """<p class="path"><a href="/">~/twopoint</a> $ ls -l posts/ --page=2</p><h1>Posts</h1>""" "The command"
      Expect.stringContains html """<meta name="robots" content="index,follow">""" "Page 2 is indexable"
    }

    test "with no post, /posts/ has one page with no log" {
      let html = page [] "posts/index.html"
      Expect.isFalse (html.Contains """<section class="log">""") "No log"
      Expect.isNone (pager html) "No pager"
      Expect.isFalse ((pagePaths (map Stage.Production [])).Contains "posts/2/index.html") "No page 2"
    }

    test "each category, series, and tag has a list of its posts" {
      let posts = [ partOne; partTwo; talk ]

      Expect.equal
        (listPaths posts)
        (set
          [ "posts/index.html"
            "category/series/index.html"
            "category/talks/index.html"
            "series/index.html"
            "series/fable-reminders/index.html"
            "tag/fsharp/index.html"
            "tag/fable/index.html"
            "tag/net/index.html"
            "tag/csharp/index.html" ])
        "One list for each category, series, and tag"

      Expect.equal (slugs (page posts "posts/index.html")) [ "part-2"; "part-1"; "a-talk" ] "Each post, the newest first"
      Expect.equal (slugs (page posts "category/talks/index.html")) [ "a-talk" ] "The category"
      Expect.equal (slugs (page posts "series/fable-reminders/index.html")) [ "part-2"; "part-1" ] "The series"
      Expect.equal (slugs (page posts "tag/fsharp/index.html")) [ "part-2"; "part-1" ] "The tag"
      Expect.equal (slugs (page posts "tag/fable/index.html")) [ "part-1" ] "The tag of one post"
    }

    test "a category page has the category title and a description" {
      let html = page [ talk ] "category/talks/index.html"
      Expect.stringContains html $"<title>Category &#39;Talks&#39; {emDash} TwoPoint</title>" "The title"
      Expect.stringContains html """<meta name="description" content="Each post in the category Talks, the newest first.">""" "The description"
      Expect.stringContains html """<link rel="canonical" href="https://twopoint.dev/category/talks/">""" "The canonical URL"
      Expect.stringContains html """<meta name="robots" content="index,follow">""" "The page is indexable"

      Expect.stringContains
        html
        """<p class="path"><a href="/">~/twopoint</a> $ ls -l category/talks/</p><h1>Talks</h1><p class="excerpt">Each post in the category Talks, the newest first.</p><section class="log">"""
        "The command, the heading, and the excerpt"
    }

    test "/category/series/ has noindex,follow and the canonical URL of /series/" {
      let html = page [ partOne; partTwo ] "category/series/index.html"
      Expect.stringContains html """<meta name="robots" content="noindex,follow">""" "Search engines do not index the page"
      Expect.stringContains html """<link rel="canonical" href="https://twopoint.dev/series/">""" "The canonical URL is /series/"
      Expect.stringContains html """<meta property="og:url" content="https://twopoint.dev/series/">""" "og:url is /series/"
      Expect.equal (slugs html) [ "part-2"; "part-1" ] "The page lists the posts of the category"
    }

    test "on staging, /category/series/ has noindex,nofollow like each page" {
      let html = siteText (map Stage.Staging [ partOne ]) "category/series/index.html"
      Expect.stringContains html """<meta name="robots" content="noindex,nofollow">""" "Staging is not indexable"
    }

    test "the other list pages have index,follow and their own canonical URL" {
      let posts = [ partOne; talk ]

      for path, url in
        [ "posts/index.html", "/posts/"
          "category/talks/index.html", "/category/talks/"
          "series/fable-reminders/index.html", "/series/fable-reminders/"
          "series/index.html", "/series/" ] do
        let html = page posts path
        Expect.stringContains html """<meta name="robots" content="index,follow">""" $"{path} is indexable"
        Expect.stringContains html $"""<link rel="canonical" href="https://twopoint.dev{url}">""" $"{path} has its own canonical URL"
    }

    test "a tag page has noindex,follow and its own canonical URL" {
      let html = page [ partOne; talk ] "tag/net/index.html"
      Expect.stringContains html """<meta name="robots" content="noindex,follow">""" "Search engines do not index the page"
      Expect.stringContains html """<link rel="canonical" href="https://twopoint.dev/tag/net/">""" "The canonical URL is the tag page"
    }

    test "a tag page shows the tag title, so that fsharp shows as F#" {
      let posts = [ partOne; talk ]

      let fsharp = page posts "tag/fsharp/index.html"
      Expect.stringContains fsharp $"<title>Posts by tag &#39;F#&#39; {emDash} TwoPoint</title>" "The title of fsharp"
      Expect.stringContains fsharp "<h1>#F#</h1>" "The heading of fsharp"
      Expect.stringContains fsharp """<meta name="description" content="Each post with the tag F#, the newest first.">""" "The description of fsharp"

      Expect.stringContains (page posts "tag/csharp/index.html") "<h1>#C#</h1>" "csharp shows as C#"
      Expect.stringContains (page posts "tag/net/index.html") "<h1>#.NET</h1>" ".NET keeps its title and gets the slug net"
      Expect.stringContains (page posts "tag/fable/index.html") $"<title>Posts by tag &#39;Fable&#39; {emDash} TwoPoint</title>" "Another tag keeps its text"
    }

    test "a series page has the series title, its excerpt, and its Open Graph image" {
      let image = "https://example.com/fable.png"

      let withImage slug date =
        published
          slug
          (text
            $"publishDate: {date}\nauthor: Matthew Watt\ntitle: {slug}\nexcerpt: An excerpt\ncategory: Series\nseries:\n  slug: fable-reminders\n  title: Fable Reminders\n  excerpt: Notes on Fable\n  image: {image}"
            "Text.")

      let posts = [ withImage "part-1" "2024-11-01T13:00:00Z"; withImage "part-2" "2024-11-15T14:00:00Z" ]
      let content = content posts
      let html = siteText (RenderMap.create Stage.Production buildTime widgets content) "series/fable-reminders/index.html"

      Expect.stringContains html $"<title>Series &#39;Fable Reminders&#39; {emDash} TwoPoint</title>" "The title"
      Expect.stringContains html """<h1>Fable Reminders</h1><p class="excerpt">Notes on Fable</p>""" "The heading and the excerpt"
      Expect.stringContains html """<meta name="description" content="Notes on Fable">""" "The description is the excerpt"

      Expect.stringContains
        html
        $"""<meta property="og:image" content="https://twopoint.dev{content.OgImages.ByPost["part-2"].UrlPath}">"""
        "The Open Graph image is the crop of the series image"

      Expect.stringContains
        (page [ partOne ] "series/fable-reminders/index.html")
        $"""<meta property="og:image" content="https://twopoint.dev{(content.Assets.DefaultImage).UrlPath}">"""
        "A series with no image gets the default image"
    }

    test "/series/ lists each series with its number of posts" {
      let html = page [ partOne; partTwo; talk ] "series/index.html"
      Expect.stringContains html $"<title>Series {emDash} TwoPoint</title>" "The title"

      Expect.stringContains
        html
        """<section class="series-list"><ul><li><a href="/series/fable-reminders/">fable-reminders/</a><span>2 posts &#183; A series</span></li></ul></section>"""
        "The series line"

      Expect.isFalse ((page [ talk ] "series/index.html").Contains "series-list") "No series gives no series list"
    }

    test "the real output has each list page" {
      let paths = pagePaths (RenderMap.create Stage.Production buildTime widgets realContent.Value)

      for path in
        [ "posts/2/index.html"
          "category/one-offs/index.html"
          "series/fable-reminders/index.html"
          "tag/functional-programming/2/index.html"
          "tag/net/index.html" ] do
        Expect.contains paths path $"The output has {path}"
    }
  ]
