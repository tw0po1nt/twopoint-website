module TwoPoint.Site.Generator.Tests.FeedTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto
open System
open System.Globalization
open System.Xml.Linq

let private older = postWith "older" "2025-11-14T01:30:00Z" "Talks" [] None
let private newer = postWith "newer" "2026-02-04T13:00:00Z" "One-offs" [] None

/// The build time of the fixture is 2026-10-18T12:00:00Z.
let private upcoming = postWith "upcoming" "2026-10-29T13:00:00Z" "One-offs" [] None

let private feed stage (posts: Post list) =
  RenderMap.create stage buildTime widgets (content posts)
  |> Map.find (SiteFile "rss.xml")
  |> fun render -> XDocument.Parse(Text.Encoding.UTF8.GetString(render ()))

let private channel (feed: XDocument) = feed.Root.Element "channel"

let private items (feed: XDocument) = (channel feed).Elements "item" |> List.ofSeq

let private value (name: string) (element: XElement) = element.Element(name).Value

/// The backend takes the slug from the link in this way.
let private slugOf (link: string) =
  (Uri link).AbsolutePath.TrimEnd('/').Split '/' |> Array.last

[<Tests>]
let tests =
  testList "Feed" [
    test "the channel has the title, the description, and the site URL" {
      let channel = channel (feed Stage.Production [ older ])
      Expect.equal (value "title" channel) $"TwoPoint{char 0x2019}s Blog" "The title"
      Expect.equal (value "description" channel) "A blog about programming, mostly functional" "The description"
      Expect.equal (value "link" channel) "https://twopoint.dev/" "The link"
    }

    test "each item has the title, the link, the guid, the description, and the publish date of its post" {
      let items = items (feed Stage.Production [ older; newer ])

      Expect.equal
        [ for item in items ->
            value "title" item, value "link" item, value "guid" item, value "description" item, value "pubDate" item ]
        [ "newer",
          "https://twopoint.dev/posts/newer/",
          "https://twopoint.dev/posts/newer/",
          "An excerpt",
          "Wed, 04 Feb 2026 13:00:00 GMT"
          "older",
          "https://twopoint.dev/posts/older/",
          "https://twopoint.dev/posts/older/",
          "An excerpt",
          "Fri, 14 Nov 2025 01:30:00 GMT" ]
        "One item for each post, the newest first"

      for item in items do
        Expect.equal (item.Element("guid").Attribute("isPermaLink").Value) "true" "The guid is the permalink"
    }

    test "the backend can read the publish date of each item" {
      let pubDate = value "pubDate" (items (feed Stage.Production [ newer ])).Head

      Expect.equal
        (DateTimeOffset.Parse(pubDate, CultureInfo.InvariantCulture))
        newer.FrontMatter.PublishDate
        "The publish date reads back as the same instant"
    }

    test "the feed includes an upcoming post" {
      let slugs = items (feed Stage.Production [ newer; upcoming ]) |> List.map (value "link" >> slugOf)
      Expect.equal slugs [ "upcoming"; "newer" ] "The upcoming post is in the feed"
    }

    test "each link uses the site URL of the stage" {
      for stage in Stage.all do
        let site = (Stage.config stage).SiteUrl.AbsoluteUri
        let item = (items (feed stage [ newer ])).Head
        Expect.equal (value "link" item) $"{site}posts/newer/" $"The {Stage.name stage} link"
    }

    test "the slug of each real feed link, after TrimEnd('/'), is the post slug" {
      let feed = feed Stage.Production realPosts.Value
      let slugs = items feed |> List.map (value "link" >> slugOf) |> List.sort
      Expect.equal slugs (realPosts.Value |> List.map _.Slug |> List.sort) "The backend finds the slug of each post"
    }
  ]
