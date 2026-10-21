module TwoPoint.Site.Generator.Tests.NewsletterTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open AngleSharp.Dom
open AngleSharp.Html.Parser
open Expecto
open System

let private body =
  "Read [the other post](/posts/a-one-off/) and [the heading](#a-heading).\n\n\
   ## A heading\n\n\
   ```fsharp\n\
   let answer = 42\n\
   ```\n\n\
   ![A cat](https://example.com/cat.jpg)\n\n\
   ::pdf{file=\"https://example.com/slides.pdf\"}\n\n\
   ::youtube{id=\"qRuKTkDvAk4\"}"

let private post = published "a-post" (text frontMatter body)

let private oneOff = postWith "a-one-off" "2025-06-01T00:00:00Z" "One-offs" [] None

let private map stage (posts: Post list) =
  RenderMap.create stage buildTime widgets (content posts)

let private parse (html: string) = HtmlParser().ParseDocument html

let private newsletter stage =
  parse (newsletterText (map stage [ post; oneOff ]) post.Slug)

let private all selector (document: IDocument) =
  document.QuerySelectorAll selector |> List.ofSeq

let private one selector (document: IDocument) =
  match all selector document with
  | [ element ] -> element
  | elements -> failtest $"The newsletter has {elements.Length} elements for '{selector}'."

let private fonts =
  "https://fonts.googleapis.com/css2?family=Inter:wght@400;600;700&family=JetBrains+Mono:wght@400;600&display=swap"

let private isAbsolute (value: string) =
  match Uri.TryCreate(value, UriKind.Absolute) with
  | true, uri -> uri.Scheme = Uri.UriSchemeHttps || uri.Scheme = Uri.UriSchemeHttp
  | false, _ -> false

[<Tests>]
let tests =
  testList "Newsletter" [
    test "the render map has one newsletter for each post" {
      let newsletters = RenderMap.newsletterSlugs (map Stage.Production [ post; oneOff ])
      Expect.equal newsletters [ "a-one-off"; "a-post" ] "One newsletter for each post"
    }

    test "the newsletter keeps the slots that the backend fills" {
      let html = newsletterText (map Stage.Production [ post; oneOff ]) post.Slug
      Expect.stringContains html Newsletter.introSlot "The intro slot"
      Expect.equal ((one $"a[href='{Newsletter.unsubscribeUrlSlot}']" (parse html)).TextContent) "$ unsubscribe" "The unsubscribe slot"
    }

    test "the intro panel markers enclose the whole panel and nothing else" {
      let html = newsletterText (map Stage.Production [ post; oneOff ]) post.Slug
      let start = html.IndexOf Newsletter.introPanelStart
      let finish = html.IndexOf Newsletter.introPanelEnd
      Expect.isGreaterThan start -1 "The start marker"
      Expect.isGreaterThan finish start "The end marker follows the start marker"

      let panel = html.Substring(start, finish + Newsletter.introPanelEnd.Length - start)
      Expect.stringContains panel Newsletter.introSlot "The intro slot is in the panel"

      let withoutPanel = parse (html.Remove(start, panel.Length))
      Expect.isEmpty (all ".tp-term" withoutPanel) "No part of the panel remains"
      Expect.equal (all "h1" withoutPanel).Length 1 "The post header remains"
      Expect.equal (all $"a[href='{Newsletter.unsubscribeUrlSlot}']" withoutPanel).Length 1 "The footer remains"
    }

    test "the newsletter has no script and no site stylesheet" {
      let document = newsletter Stage.Production
      Expect.isEmpty (all "script" document) "No script"

      Expect.equal
        [ for link in all "link" document -> link.GetAttribute "rel", link.GetAttribute "href" ]
        [ "stylesheet", fonts ]
        "The Google Fonts link is the one link"
    }

    test "the embed CSS is the one style block, as written" {
      let style = one "style" (newsletter Stage.Production)
      Expect.isTrue (style.HasAttribute "data-embed") "The block is the embed block"
      Expect.equal style.TextContent realContent.Value.Email.Embed "The block contains the embed CSS as written"
    }

    test "a code token and a body image get their inline styles" {
      let document = newsletter Stage.Production
      let keyword = all "pre code .tk-keyword" document |> List.head
      Expect.equal (keyword.GetAttribute "style") "color: #f97583" "The github-dark keyword color"
      Expect.stringContains ((one "pre" document).GetAttribute "style") "white-space: pre-wrap" "The code wraps"
      Expect.stringContains ((one "pre" document).GetAttribute "style") "background-color: #24292e" "The code stays dark"

      let image = one ".tp-post img[src='https://example.com/cat.jpg']" document
      Expect.stringContains (image.GetAttribute "style") "max-width: 100%" "The image fits the column"
      Expect.isFalse (image.HasAttribute "width") "The image has no width that Outlook can show"
    }

    test "the header has the label, the title, the date, the reading time, and the tags" {
      let seriesPost =
        publishedIn
          [ fableReminders ]
          "a-series-post"
          (text
            "publishDate: 2025-09-01T00:00:00Z\nauthor: Matthew Watt\ntitle: A series post\nexcerpt: An excerpt\n\
             category: Series\ntags:\n  - FSharp\nseries: fable-reminders"
            "Text.")

      let document = parse (newsletterText (map Stage.Production [ seriesPost ]) seriesPost.Slug)
      Expect.equal (one ".tp-label" document).TextContent "// latest post · fable reminders" "The label names the series"
      Expect.equal (one "h1" document).TextContent "A series post" "The title"
      Expect.equal (one ".tp-meta" document).TextContent "2025-09-01 · 1 min · #fsharp" "The date, the time, and the tags"
      Expect.equal (one ".tp-excerpt" document).TextContent "An excerpt" "The excerpt"
      Expect.equal (one ".tp-preheader" document).TextContent "An excerpt" "The preview text"
    }

    test "::pdf gives one link to the post, and no Pdf mount" {
      let document = newsletter Stage.Production
      let links = all ".tp-post a" document |> List.filter (fun link -> link.TextContent = "See the PDF in the post")
      Expect.equal [ for link in links -> link.GetAttribute "href" ] [ "https://twopoint.dev/posts/a-post/" ] "One link to the post"
      Expect.isEmpty (all "[data-widget], figure" document) "No mount"
    }

    test "::youtube gives the video thumbnail in a link to the video, and no iframe" {
      let document = newsletter Stage.Production
      let image = one "a[href='https://www.youtube.com/watch?v=qRuKTkDvAk4'] > img" document
      Expect.equal (image.GetAttribute "src") "https://img.youtube.com/vi/qRuKTkDvAk4/hqdefault.jpg" "The thumbnail"
      Expect.isEmpty (all "iframe" document) "No iframe"
    }

    test "a root-relative link and a link to a heading become absolute with the site URL of the stage" {
      for stage in Stage.all do
        let site = (Stage.config stage).SiteUrl.AbsoluteUri
        let document = newsletter stage
        Expect.equal ((one ".tp-post a[href$='/a-one-off/']" document).GetAttribute "href") $"{site}posts/a-one-off/" "The post link"
        Expect.equal ((one ".tp-post a[href$='#a-heading']" document).GetAttribute "href") $"{site}posts/a-post/#a-heading" "The heading link"
    }

    test "the button, the site link, and the privacy link use the site URL of the stage" {
      let document = newsletter Stage.Staging
      Expect.equal ((one ".tp-button a" document).GetAttribute "href") "https://staging.twopoint.dev/posts/a-post/" "The post"

      Expect.equal
        [ for link in all ".tp-footer a" document -> link.GetAttribute "href" ]
        [ "https://staging.twopoint.dev/"; Newsletter.unsubscribeUrlSlot; "https://staging.twopoint.dev/privacy/" ]
        "The footer links"
    }

    test "each href and src in each newsletter of the real posts and the fixture is absolute" {
      for stage in Stage.all do
        let real = RenderMap.create stage buildTime widgets realContent.Value
        let fixture = map stage [ post; oneOff ]

        for map, slugs in [ real, realPosts.Value |> List.map _.Slug; fixture, [ post.Slug; oneOff.Slug ] ] do
          for slug in slugs do
            let document = parse (newsletterText map slug)

            for element in all "[href], [src]" document do
              for name in [ "href"; "src" ] do
                match element.GetAttribute name with
                | null -> ()
                | value when value = Newsletter.unsubscribeUrlSlot -> ()
                | value -> Expect.isTrue (isAbsolute value) $"The {name} '{value}' of the {Stage.name stage} newsletter of {slug}"
    }
  ]
