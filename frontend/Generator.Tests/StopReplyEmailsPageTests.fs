module TwoPoint.Site.Generator.Tests.StopReplyEmailsPageTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture
open TwoPoint.Site.Render
open TwoPoint.Site.Render.Tests.Fixture

open Expecto

let private post = postWith "a-post" "2026-01-01T00:00:00Z" "One-offs" [] None

let private map stage = RenderMap.create stage buildTime widgets (content [ post ])

let private page = "comments/stop/index.html"

let private mount api =
  $"""<div data-widget="stop-reply-emails" data-api="{api}"><p class="stop-reply-emails-fallback">The stop link needs JavaScript.</p></div>"""

let private help =
  """<p class="stop-reply-emails-help">If the link doesn&#39;t work, email <a href="mailto:matt@twopoint.dev">matt@twopoint.dev</a>.</p>"""

[<Tests>]
let tests =
  testList "Stop reply emails page" [
    test "/comments/stop/ has the title, the canonical URL, the StopReplyEmails mount, and the help line" {
      let html = siteText (map Stage.Production) page
      Expect.stringContains html $"<title>Stop reply emails {string (char 0x2014)} TwoPoint</title>" "The title"
      Expect.stringContains html """<link rel="canonical" href="https://twopoint.dev/comments/stop/">""" "The canonical URL"
      Expect.stringContains html $"""<h1>Stop reply emails</h1>{mount "https://func-twopoint-website-api.azurewebsites.net"}{help}</main>""" "The mount follows the heading"
    }

    test "the mount gives the API origin of the stage" {
      Expect.stringContains (siteText (map Stage.Local) page) (mount "http://localhost:7071") "Local calls the local API"
    }

    test "/comments/stop/ asks search engines not to index it, and the sitemap omits it" {
      Expect.stringContains (siteText (map Stage.Production) page) """<meta name="robots" content="noindex,follow">""" "No index"
      Expect.isFalse ((siteText (map Stage.Production) "sitemap-0.xml").Contains "/comments/stop/") "Not in the sitemap"
    }

    test "/comments/stop/ loads the StopReplyEmails entry, and the other pages do not" {
      let entry = """<script type="module" src="/_/widgets/stop-reply-emails-TEST.js"></script>"""
      Expect.stringContains (siteText (map Stage.Local) page) entry "The page loads the entry"
      Expect.isFalse ((siteText (map Stage.Local) "unsubscribe/index.html").Contains entry) "/unsubscribe/ does not load the entry"
    }
  ]
