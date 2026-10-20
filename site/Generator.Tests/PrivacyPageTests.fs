module TwoPoint.Site.Generator.Tests.PrivacyPageTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto

let private privacyFile = "site/content/privacy.md"

let private parse text = MarkdownPage.parse anyImages privacyFile text

let private privacyText (frontMatter: string) (body: string) = $"---\n{frontMatter}\n---\n\n{body}\n"

let private page body =
  parse (privacyText "title: Privacy policy\ndescription: A description" body) |> ok

let private errorAt line message = PostError.at privacyFile line message

let private map stage = RenderMap.create stage buildTime widgets (content [])

[<Tests>]
let tests =
  testList "Privacy page" [
    test "/privacy/ has the title, the description, the canonical URL, and the robots value" {
      let html = siteText (map Stage.Production) "privacy/index.html"
      Expect.stringContains html $"<title>Privacy policy {string (char 0x2014)} TwoPoint</title>" "The title"

      Expect.stringContains
        html
        """<meta name="description" content="The data that TwoPoint collects, why it collects the data, and how long it keeps the data.">"""
        "The description comes from the front matter"

      Expect.stringContains html """<link rel="canonical" href="https://twopoint.dev/privacy/">""" "The canonical URL"
      Expect.stringContains html """<meta name="robots" content="index,follow">""" "The page is indexable"
    }

    test "the body is the Markdown of the privacy file, with a heading id on each heading" {
      let html = siteText (map Stage.Production) "privacy/index.html"
      Expect.stringContains html """<h1>Privacy policy</h1><div class="prose">""" "The body follows the title"
      Expect.stringContains html """<h2 id="browser-storage">Browser storage</h2>""" "Each heading has its id"
      Expect.stringContains html """<h2 id="embedded-videos">Embedded videos</h2>""" "The policy has the embed section"
    }

    test "the policy names each storage key and the YouTube embed host" {
      let html = siteText (map Stage.Production) "privacy/index.html"

      for key in [ "theme"; "commenterValidations"; "pulse_ignore"; "youtube-nocookie.com" ] do
        Expect.stringContains html $"<code>{key}</code>" $"The policy names {key}"
    }

    test "the page gets the front matter and the body of its file" {
      let page = page "## Who runs the site\n\nRead the [posts](/posts/)."
      Expect.equal page.Title "Privacy policy" "The title"
      Expect.equal page.Description "A description" "The description"
      Expect.equal page.Body.Anchors (set [ "who-runs-the-site" ]) "The heading gives an anchor"
      Expect.equal page.Body.Links [ { Target = Page("/posts/", None); Line = 8 } ] "The internal link has its line"
    }

    test "a missing field, an unknown field, or no front matter fails" {
      Expect.equal
        (parse (privacyText "title: Privacy policy" "Text."))
        (Error [ errorAt 1 "The front matter has no 'description' field." ])
        "The description is required"

      Expect.equal
        (parse (privacyText "title: Privacy policy\ndescription: A description\ndraft: true" "Text."))
        (Error [ errorAt 4 "Unknown field 'draft'." ])
        "An unknown field fails"

      Expect.equal
        (parse "Text.")
        (Error [ errorAt 1 "The page has no front matter. Start the file with a '---' line." ])
        "The front matter is required"
    }

    test "a link from the page to a path that is not in the output fails the build" {
      let content = { content [] with Privacy = page "Text.\n\nRead [c](/posts/c-post/)." }

      Expect.equal
        (Site.renderMap Stage.Production buildTime widgets content |> Result.map ignore)
        (Error [ $"{privacyFile}:8: The output has no page at /posts/c-post/." ])
        "The build checks the links of the privacy page"
    }

    test "a link from a post to a heading of the privacy page passes" {
      let post = published "a-post" (text frontMatter "Read [the storage](/privacy/#browser-storage).")
      let content = { content [ post ] with Privacy = page "## Browser storage\n\nText." }
      Expect.isOk (Site.renderMap Stage.Production buildTime widgets content) "The heading is on the privacy page"
    }
  ]
