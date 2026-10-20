module TwoPoint.Site.Generator.Tests.UnsubscribePageTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto

let private post = postWith "a-post" "2026-01-01T00:00:00Z" "One-offs" [] None

let private map stage = RenderMap.create stage buildTime widgets (content [ post ])

let private mount api =
  $"""<div data-widget="unsubscribe" data-api="{api}"><p class="unsubscribe-fallback">The unsubscribe link needs JavaScript.</p></div>"""

let private help =
  """<p class="unsubscribe-help">If the link does not work, send an email to <a href="mailto:matt@twopoint.dev">matt@twopoint.dev</a>.</p>"""

[<Tests>]
let tests =
  testList "Unsubscribe page" [
    test "/unsubscribe/ has the title, the canonical URL, the Unsubscribe mount, and the help line" {
      let html = siteText (map Stage.Production) "unsubscribe/index.html"
      Expect.stringContains html $"<title>Unsubscribe {string (char 0x2014)} TwoPoint</title>" "The title"
      Expect.stringContains html """<link rel="canonical" href="https://twopoint.dev/unsubscribe/">""" "The canonical URL"
      Expect.stringContains html $"""<h1>Unsubscribe</h1>{mount "https://func-twopoint-website-api.azurewebsites.net"}{help}</main>""" "The mount follows the heading"
    }

    test "the mount gives the API origin of the stage" {
      Expect.stringContains (siteText (map Stage.Local) "unsubscribe/index.html") (mount "http://localhost:7071") "Local calls the local API"
    }

    test "/unsubscribe/ loads the Unsubscribe entry, and the other pages do not" {
      let entry = """<script type="module" src="/_/widgets/unsubscribe-TEST.js"></script>"""
      Expect.stringContains (siteText (map Stage.Local) "unsubscribe/index.html") entry "The page loads the entry"
      Expect.isFalse ((siteText (map Stage.Local) "subscribe/index.html").Contains entry) "/subscribe/ does not load the entry"
    }
  ]
