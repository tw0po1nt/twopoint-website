module TwoPoint.Site.Render.Tests.ArticleTests

open TwoPoint.Site.Render
open TwoPoint.Site.Render.Tests.Fixture

open Expecto
open Oxpecker.ViewEngine

let private series = seriesRecord "fable-reminders"

let private first = postWith "fable-reminders-part-1" "2024-11-01T00:00:00Z" "Series" [] (Some series.Slug)

let private second =
  publishedIn
    [ series ]
    "fable-reminders-part-2"
    (text
      "publishDate: 2024-11-15T00:00:00Z\nupdateDate: 2024-12-01T00:00:00Z\nauthor: Matthew Watt\ntitle: Fable Reminders - Part 2\nexcerpt: The second part\ncategory: Series\ntags:\n  - FSharp\n  - Fable\nseries: fable-reminders"
      "## First\n\nText.\n\n### Not in the outline\n\nText.\n\n#### Detail\n\nText.")

let private content (posts: Post list) (post: Post) =
  Article.content post (Blog.seriesPosition posts post) |> Render.toString

[<Tests>]
let tests =
  testList "Article" [
    test "the meta block has the dates, the author, the series position, the tags, and the reading time" {
      let html = content [ first; second ] second
      Expect.stringContains html """<dt>date</dt><dd><time datetime="2024-11-15T00:00:00Z">2024-11-15</time></dd>""" "The publish date"
      Expect.stringContains html """<dt>updated</dt><dd><time datetime="2024-12-01T00:00:00Z">2024-12-01</time></dd>""" "The update date"
      Expect.stringContains html """<dt>author</dt><dd>Matthew Watt</dd>""" "The author"
      Expect.stringContains html """<a href="/series/fable-reminders/">fable-reminders</a> (2 of 2)</dd>""" "The series position"
      Expect.stringContains html """<a href="/tag/fsharp/">#F#</a> <a href="/tag/fable/">#Fable</a>""" "The tags"
      Expect.stringContains html """<dt>read</dt><dd>1 min</dd>""" "The reading time"
    }

    test "the title, the excerpt, and the body follow the meta block" {
      Expect.stringContains
        (content [ first; second ] second)
        """</dl><h1>Fable Reminders - Part 2</h1><p class="excerpt">The second part</p><div class="prose"><h2 id="first">First</h2>"""
        "The order of the article"
    }

    test "the outline lists the h2 and h4 headings" {
      Expect.stringContains
        (Article.outline second |> Render.toString)
        """<ul><li class="outline-h2"><a href="#first">First</a></li><li class="outline-h4"><a href="#detail">Detail</a></li></ul>"""
        "The outline has the h2 and the h4, and no h3"
    }

    test "the outline text of a heading has curly quote marks" {
      let post = published "a-post" (text frontMatter "## It's a \"test\"")
      Expect.stringContains (Article.outline post |> Render.toString) """<a href="#its-a-test">It’s a “test”</a>""" "The outline entry"
    }
  ]
