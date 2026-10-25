module TwoPoint.Dependencies.Tests.PublishedParseCacheTests

open TwoPoint.Core.Posts
open TwoPoint.Dependencies.Posts
open TwoPoint.Site.Render

open Expecto

let private text title =
  $"---\npublishDate: 2026-01-01T00:00:00Z\nauthor: A\ntitle: {title}\nexcerpt: E\ncategory: One-offs\n---\n\nBody.\n"

let private stored name title eTag : StoredPost =
  { Slug = Slug.Unsafe.create name; Text = text title; ETag = eTag }

let private record eTag : StoredSeries =
  { Slug = "tour"; Text = "title: Tour\nexcerpt: A tour\n"; ETag = eTag }

[<Tests>]
let tests =
  testList "PublishedParseCache" [
    test "a post with the same ETag keeps its parse, and a post with a new ETag is parsed again" {
      let cache = PublishedParseCache Stage.Staging
      let _, first = cache.Parse [ stored "a" "First" "v1"; stored "b" "Second" "v1" ] []
      let _, second = cache.Parse [ stored "a" "First" "v1"; stored "b" "Second, edited" "v2" ] []

      Expect.isTrue (obj.ReferenceEquals(first[0], second[0])) "The post with the same ETag"
      Expect.equal second[1].FrontMatter.Title "Second, edited" "The post with a new ETag"
    }

    test "a change of a series record parses the posts again" {
      let cache = PublishedParseCache Stage.Staging
      let posts = [ stored "a" "First" "v1" ]
      let _, first = cache.Parse posts [ record "s1" ]
      let _, second = cache.Parse posts [ record "s2" ]

      Expect.isFalse (obj.ReferenceEquals(first[0], second[0])) "A new parse"
    }

    test "a post that does not parse is left out" {
      let cache = PublishedParseCache Stage.Staging
      let broken : StoredPost = { Slug = Slug.Unsafe.create "broken"; Text = "no front matter"; ETag = "v1" }
      let _, posts = cache.Parse [ broken; stored "a" "First" "v1" ] []

      Expect.equal (posts |> List.map _.Slug) [ "a" ] "Only the valid post"
    }
  ]
