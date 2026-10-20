module TwoPoint.Site.Generator.Tests.BlogTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto

let private slugs (posts: Post list) = posts |> List.map _.Slug

[<Tests>]
let tests =
  testList "Blog" [
    test "the related score gives 5 for the same category and 1 for each shared tag" {
      let post = postWith "post" "2026-01-01T00:00:00Z" "One-offs" [ "FSharp"; "Fable"; "Web" ] None

      let score category tags =
        Blog.relatedScore post (postWith "other" "2026-01-02T00:00:00Z" category tags None)

      Expect.equal (score "One-offs" []) 5 "The same category gives 5"
      Expect.equal (score "Talks" [ "FSharp"; "Web"; "Swift" ]) 2 "Each shared tag gives 1"
      Expect.equal (score "One-offs" [ "fsharp" ]) 6 "Tags compare by slug"
      Expect.equal (score "Talks" [ "Swift" ]) 0 "A post with no shared category or tag gives 0"
    }

    test "the related posts are the top 4 by score, with the newest first in a tie" {
      let post = postWith "post" "2026-01-01T00:00:00Z" "One-offs" [ "FSharp"; "Web" ] None

      let posts =
        [ post
          postWith "talk-one-tag" "2025-01-01T00:00:00Z" "Talks" [ "FSharp" ] None
          postWith "talk-two-tags" "2025-02-01T00:00:00Z" "Talks" [ "FSharp"; "Web" ] None
          postWith "old-one-off" "2024-01-01T00:00:00Z" "One-offs" [] None
          postWith "new-one-off" "2026-02-01T00:00:00Z" "One-offs" [] None
          postWith "one-off-with-tag" "2023-01-01T00:00:00Z" "One-offs" [ "Web" ] None
          postWith "unrelated" "2026-03-01T00:00:00Z" "Talks" [ "Swift" ] None ]

      Expect.equal
        (Blog.related posts post |> slugs)
        [ "one-off-with-tag"; "new-one-off"; "old-one-off"; "talk-two-tags" ]
        "6 points, then 5 points with the newest first, then 2 points"
    }

    test "a post with a score of 0 is not related" {
      let post = postWith "post" "2026-01-01T00:00:00Z" "One-offs" [] None
      let posts = [ post; postWith "talk" "2025-01-01T00:00:00Z" "Talks" [ "Swift" ] None ]
      Expect.isEmpty (Blog.related posts post) "A talk with no shared tag is not related to a one-off"
    }

    test "the older and newer posts come from the publish date" {
      let oldest = postWith "a-oldest" "2024-01-01T00:00:00Z" "One-offs" [] None
      let middle = postWith "c-middle" "2025-01-01T00:00:00Z" "Talks" [] None
      let newest = postWith "b-newest" "2026-01-01T00:00:00Z" "One-offs" [] None
      let posts = [ oldest; middle; newest ]

      let neighbors post =
        let neighbors = Blog.neighbors posts post
        neighbors.Older |> Option.map _.Slug, neighbors.Newer |> Option.map _.Slug

      Expect.equal (neighbors middle) (Some "a-oldest", Some "b-newest") "The middle post has both cards"
      Expect.equal (neighbors newest) (Some "c-middle", None) "The newest post has no newer post"
      Expect.equal (neighbors oldest) (None, Some "c-middle") "The oldest post has no older post"
    }

    test "the series position comes from the publish date order in the series" {
      let first = postWith "z-first" "2024-01-01T00:00:00Z" "Series" [] (Some "fable-reminders")
      let second = postWith "a-second" "2024-06-01T00:00:00Z" "Series" [] (Some "fable-reminders")
      let other = postWith "other-series" "2024-03-01T00:00:00Z" "Series" [] (Some "footguns")
      let oneOff = postWith "one-off" "2024-04-01T00:00:00Z" "One-offs" [] None
      let posts = [ second; oneOff; other; first ]

      let position post =
        Blog.seriesPosition posts post |> Option.map (fun position -> position.Position, position.Count)

      Expect.equal (position first) (Some(1, 2)) "The oldest post of the series is first"
      Expect.equal (position second) (Some(2, 2)) "The newer post of the series is second"
      Expect.equal (position other) (Some(1, 1)) "Each series counts its own posts"
      Expect.isNone (position oneOff) "A post with no series has no position"
    }

    test "the years, the series, and the tags of the explorer" {
      let posts =
        [ postWith "a" "2025-03-01T00:00:00Z" "Series" [ "FSharp" ] (Some "fable-reminders")
          postWith "b" "2026-01-01T00:00:00Z" "Talks" [ "Swift"; "FSharp" ] None
          postWith "c" "2025-09-01T00:00:00Z" "Series" [ "Swift"; "FSharp" ] (Some "fable-reminders") ]

      Expect.equal
        (Blog.byYear posts |> List.map (fun (year, posts) -> year, slugs posts))
        [ 2026, [ "b" ]; 2025, [ "c"; "a" ] ]
        "The newest year and the newest post come first"

      Expect.equal
        (Blog.series posts |> List.map (fun (series, count) -> series.Slug, count))
        [ "fable-reminders", 2 ]
        "Each series has its number of posts"

      Expect.equal
        (Blog.tags posts |> List.map (fun (tag, count) -> tag.Slug, count))
        [ "fsharp", 3; "swift", 2 ]
        "The tag with the most posts comes first"
    }
  ]
