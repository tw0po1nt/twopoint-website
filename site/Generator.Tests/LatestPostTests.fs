module TwoPoint.Site.Generator.Tests.LatestPostTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto
open System.Text.RegularExpressions

let private slug = "danger-lurks-in-system-linq-enumerable-or-default-functions"

let private latest () =
  realPosts.Value |> List.find (fun post -> post.Slug = slug)

[<Tests>]
let tests =
  testList "The latest post" [
    test "the post page is in the output of each stage" {
      for stage in Stage.all do
        let map = RenderMap.create stage realContent.Value
        let page = siteText map $"posts/{slug}/index.html"
        Expect.stringContains page "<h1>Danger lurks in System.Linq.Enumerable&#39;s &quot;OrDefault&quot; functions</h1>" "The page has the title"
        Expect.stringContains page "<time datetime=\"2026-02-04T13:00:00Z\">2026-02-04</time>" "The page has the date"
        Expect.stringContains page "In this post, we&#39;ll look at how my misunderstanding" "The page has the excerpt"
        Expect.stringContains page "<h2 id=\"linq\">LINQ</h2>" "The page has the body"
    }

    test "the heading ids agree with the Astro output" {
      let page = siteText (RenderMap.create Stage.Production realContent.Value) $"posts/{slug}/index.html"
      let ids = [ for found in Regex.Matches(page, """<h[1-6] id="([^"]+)">""") -> found.Groups[1].Value ]

      Expect.equal
        ids
        [ "linq"
          "systemlinqenumerable"
          "ordefault-functions-in-systemlinqenumerable"
          "the-symptoms"
          "combining-maybe-and-systemlinqenumerable"
          "the-footgun-nullable-value-types-and-nullable-reference-types-are-overloaded"
          "value-types"
          "reference-types"
          "default-values"
          "what-about-nullable-value-types"
          "the-solution"
          "attempt-1"
          "what-actually-works" ]
        "The ids are the Astro ids, in order"
    }

    test "the reading time agrees with the Astro value" {
      Expect.equal (latest ()).Body.ReadingTime 11 "Astro gives 11 minutes"
    }

    test "the tag and category slugs agree with the Astro values" {
      let frontMatter = (latest ()).FrontMatter
      Expect.equal frontMatter.Category.Slug "series" "Astro gives /category/series"

      Expect.equal
        (frontMatter.Tags |> List.map _.Slug)
        [ "footguns"; "csharp"; "linq"; "net"; "functional-programming"; "2026" ]
        "Astro gives these tag paths"

      Expect.equal
        (frontMatter.Series |> Option.map _.Slug)
        (Some "matts-footgun-collection")
        "Astro gives /series/matts-footgun-collection"
    }

    test "each pending post is on the pending URL list and has no Markdown copy" {
      for pending in PendingPosts.slugs do
        Expect.contains UrlParity.pending $"posts/{pending}/index.html" $"The page of {pending} is pending"
        Expect.isFalse (realPosts.Value |> List.exists (fun post -> post.Slug = pending)) $"{pending} is not Markdown yet"
    }
  ]
