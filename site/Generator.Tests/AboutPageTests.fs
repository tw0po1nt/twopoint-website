module TwoPoint.Site.Generator.Tests.AboutPageTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto

let private map stage = RenderMap.create stage buildTime widgets (content [])

[<Tests>]
let tests =
  testList "About page" [
    test "/about/ has the title, the canonical URL, and the robots value" {
      let html = siteText (map Stage.Production) "about/index.html"
      Expect.stringContains html $"<title>About {string (char 0x2014)} TwoPoint</title>" "The title"
      Expect.stringContains html """<link rel="canonical" href="https://twopoint.dev/about/">""" "The canonical URL"
      Expect.stringContains html """<meta property="og:url" content="https://twopoint.dev/about/">""" "The og:url"
      Expect.stringContains html """<meta name="robots" content="index,follow">""" "The page is indexable"
    }

    test "the photo has its hashed path and the size of the file" {
      let map = map Stage.Production
      let photo = realContent.Value.Assets.AboutPhoto
      Expect.isMatch photo.UrlPath "^/_/about-[0-9a-f]{8}\\.jpg$" "The photo is under /_/ with a hash"
      Expect.equal photo.Size { Width = 800; Height = 1020 } "The size comes from the file"

      Expect.stringContains
        (siteText map "about/index.html")
        $"""<img class="about-photo" src="{photo.UrlPath}" alt="A photo of Matthew Watt" width="800" height="1020">"""
        "The page shows the photo"

      Expect.isTrue (map.ContainsKey(SiteFile(photo.UrlPath.TrimStart '/'))) "The output has the photo"
    }

    test "the page links the email address and GitHub" {
      let html = siteText (map Stage.Production) "about/index.html"
      Expect.stringContains html """<a href="mailto:matt@twopoint.dev">matt@twopoint.dev</a>""" "The email link"
      Expect.stringContains html """<a href="https://github.com/tw0po1nt" target="_blank" rel="noopener noreferrer">GitHub</a>""" "The GitHub link"
    }
  ]
