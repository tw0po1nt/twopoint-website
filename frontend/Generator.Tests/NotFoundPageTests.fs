module TwoPoint.Site.Generator.Tests.NotFoundPageTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto
open System.Text.RegularExpressions

let private post = postWith "a-post" "2026-01-01T00:00:00Z" "One-offs" [ "FSharp" ] (Some "fable-reminders")

let private html stage =
  siteText (RenderMap.create stage buildTime widgets (content [ post ])) "404.html"

[<Tests>]
let tests =
  testList "404 page" [
    test "404.html is at the root of the site, with the title" {
      Expect.stringContains (html Stage.Production) $"<title>Page not found {string (char 0x2014)} TwoPoint</title>" "The title"
    }

    test "404.html has noindex,follow, and noindex,nofollow on a stage that is not indexable" {
      Expect.stringContains (html Stage.Production) """<meta name="robots" content="noindex,follow">""" "Production"
      Expect.stringContains (html Stage.Staging) """<meta name="robots" content="noindex,nofollow">""" "Staging"
      Expect.stringContains (html Stage.Local) """<meta name="robots" content="noindex,nofollow">""" "Local"
    }

    test "404.html has no canonical URL and no og:url" {
      for stage in Stage.all do
        let html = html stage
        Expect.isFalse (html.Contains """rel="canonical""") $"The {Stage.name stage} page has no canonical URL"
        Expect.isFalse (html.Contains """property="og:url""") $"The {Stage.name stage} page has no og:url"
    }

    test "each link and asset path of 404.html starts at the root" {
      for stage in Stage.all do
        let html = html stage
        let paths = [ for found in Regex.Matches(html, """\s(?:href|src)="([^"]*)""") -> found.Groups[1].Value ]
        Expect.isNonEmpty paths "The page has links and assets"

        let relative =
          paths
          |> List.filter (fun path ->
            not ((path.StartsWith '/' && not (path.StartsWith "//")) || Regex.IsMatch(path, "^(https?|mailto):")))

        Expect.isEmpty relative $"Each path of the {Stage.name stage} page starts at the root"
    }
  ]
