module TwoPoint.Site.Generator.Tests.SubscribePageTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto

let private post = postWith "a-post" "2026-01-01T00:00:00Z" "One-offs" [] None

let private map stage = RenderMap.create stage buildTime widgets (content [ post ])

let private mount api =
  $"""<div data-widget="subscribe" data-api="{api}"><p class="subscribe-fallback">The subscribe form needs JavaScript. You can also follow the <a href="/rss.xml">RSS feed</a>.</p></div>"""

[<Tests>]
let tests =
  testList "Subscribe page" [
    test "/subscribe/ has the title, the canonical URL, and the Subscribe mount" {
      let html = siteText (map Stage.Production) "subscribe/index.html"
      Expect.stringContains html $"<title>Subscribe {string (char 0x2014)} TwoPoint</title>" "The title"
      Expect.stringContains html """<link rel="canonical" href="https://twopoint.dev/subscribe/">""" "The canonical URL"
      Expect.stringContains html """<meta name="robots" content="index,follow">""" "The page is indexable"
      Expect.stringContains html $"""<p class="excerpt">Get an email for each new post. Each email has a link to unsubscribe.</p>{mount "https://func-twopoint-website-api.azurewebsites.net"}</main>""" "The mount follows the excerpt"
    }

    test "each mount gives the API origin of the stage, with no '/' at the end" {
      Expect.stringContains (siteText (map Stage.Local) "subscribe/index.html") (mount "http://localhost:7071") "Local calls the local API"
      Expect.stringContains (siteText (map Stage.Staging) "subscribe/index.html") (mount "https://func-twopoint-website-api.azurewebsites.net") "Staging calls the deployed API"
    }

    test "the explorer of each page has a Subscribe mount before its footer links" {
      for path in [ "posts/a-post/index.html"; "subscribe/index.html" ] do
        Expect.stringContains
          (siteText (map Stage.Local) path)
          $"""<p class="label">subscribe</p>{mount "http://localhost:7071"}<p class="explorer-foot">"""
          $"The explorer of {path} has the mount"
    }
  ]
