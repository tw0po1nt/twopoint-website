module TwoPoint.Site.Generator.Tests.FrontMatterTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto
open System

let private withField field = text $"{frontMatter}\n{field}" "Body text."

let private without (field: string) =
  let lines =
    frontMatter.Split '\n' |> Array.filter (fun line -> not (line.StartsWith $"{field}:")) |> String.concat "\n"

  text lines "Body text."

[<Tests>]
let tests =
  testList "Front matter" [
    test "a post reads each field" {
      let fields =
        "updateDate: 2026-03-01T09:30:00.0000000Z\n\
         tags:\n  - CSharp\n  - .NET\n  - '2026'\n\
         series:\n  slug: matts-footgun-collection\n  title: Matt's Footgun Collection\n  excerpt: Footguns\n  image: https://example.com/series.jpg\n\
         image: https://example.com/post.jpg\n\
         showOnHomepage: false"

      let post = published "a-post" (withField fields)

      Expect.equal
        post.FrontMatter
        { PublishDate = DateTimeOffset(2026, 2, 4, 13, 0, 0, TimeSpan.Zero)
          UpdateDate = Some(DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.Zero))
          Draft = false
          Title = "A post"
          Excerpt = "An excerpt"
          Author = "Matthew Watt"
          Category = { Slug = "one-offs"; Title = "One-offs" }
          Tags =
            [ { Slug = "csharp"; Title = "C#" }
              { Slug = "net"; Title = ".NET" }
              { Slug = "2026"; Title = "2026" } ]
          Series =
            Some
              { Slug = "matts-footgun-collection"
                Title = "Matt's Footgun Collection"
                Excerpt = "Footguns"
                Image = Some(Uri "https://example.com/series.jpg") }
          Image = Some(Uri "https://example.com/post.jpg")
          ShowOnHomepage = false }
        "The post has each field"

      Expect.equal post.Slug "a-post" "The slug is the file name"
    }

    test "an optional field takes its default" {
      let frontMatter = (published "a-post" (text frontMatter "Body text.")).FrontMatter
      Expect.isFalse frontMatter.Draft "A post is not a draft by default"
      Expect.isTrue frontMatter.ShowOnHomepage "A post shows on the home page by default"
      Expect.isEmpty frontMatter.Tags "A post has no tags by default"
      Expect.isNone frontMatter.Series "A post has no series by default"
      Expect.isNone frontMatter.UpdateDate "A post has no update date by default"
      Expect.isNone frontMatter.Image "A post has no image by default"
    }

    test "an unknown field fails with its line" {
      Expect.equal
        (errors "a-post" (withField "metadata:\n  canonical: https://twopoint.dev/posts/a-post"))
        [ errorAt "a-post" 7 "Unknown field 'metadata'." ]
        "metadata is not a field"
    }

    test "each removed field fails" {
      for field in [ "showHeroImage: true"; "metadata: {}" ] do
        Expect.isNonEmpty (errors "a-post" (withField field)) $"'{field}' is not a field"
    }

    test "an unknown series field fails with its line" {
      let series = "series:\n  slug: a-series\n  title: A series\n  excerpt: Posts\n  next: b-post\n  previous: c-post"

      Expect.equal
        (errors "a-post" (withField series))
        [ errorAt "a-post" 11 "Unknown field 'series.next'."
          errorAt "a-post" 12 "Unknown field 'series.previous'." ]
        "series.next and series.previous are not fields"
    }

    test "each missing required field fails" {
      for field in [ "publishDate"; "author"; "title"; "excerpt"; "category" ] do
        Expect.equal
          (errors "a-post" (without field))
          [ errorAt "a-post" 1 $"The front matter has no '{field}' field." ]
          $"{field} is required"
    }

    test "a missing series field fails at the first line of the series" {
      Expect.equal
        (errors "a-post" (withField "series:\n  slug: a-series\n  excerpt: Posts"))
        [ errorAt "a-post" 8 "The front matter has no 'series.title' field." ]
        "series.title is required"
    }

    test "a bad value fails with its line" {
      let cases =
        [ "draft: yes", "Give 'draft' as true or false."
          "updateDate: 2026-02-04T13:00:00+02:00", "Give 'updateDate' as an ISO 8601 UTC time, for example 2026-02-04T13:00:00Z."
          "updateDate: soon", "Give 'updateDate' as an ISO 8601 UTC time, for example 2026-02-04T13:00:00Z."
          "image: http://example.com/a.jpg", "Give 'image' as an https URL."
          "tags: CSharp", "Give 'tags' as a list."
          "series: a-series", "Give 'series' as a mapping." ]

      for field, message in cases do
        Expect.equal (errors "a-post" (withField field)) [ errorAt "a-post" 7 message ] $"'{field}' fails"
    }

    test "a series slug that is not a slug fails" {
      Expect.equal
        (errors "a-post" (withField "series:\n  slug: A Series\n  title: A series\n  excerpt: Posts"))
        [ errorAt "a-post" 8 "Give 'series.slug' as a slug, for example a-series." ]
        "The series slug must be a slug"
    }

    test "each bad field of a post fails" {
      let fields = frontMatter.Replace("author: Matthew Watt\n", "") + "\ndraft: maybe"

      Expect.equal
        (errors "a-post" (text fields "Body text."))
        [ errorAt "a-post" 1 "The front matter has no 'author' field."
          errorAt "a-post" 6 "Give 'draft' as true or false." ]
        "The post gives each error"
    }

    test "bad YAML fails with its line" {
      let error = errors "a-post" (withField "tags: [CSharp") |> List.exactlyOne
      Expect.equal error.File (file "a-post") "The error names the post file"
      Expect.isGreaterThanOrEqual error.Line 7 "The error gives the line of the bad YAML"
    }

    test "a post with no front matter fails" {
      Expect.equal
        (errors "a-post" "## A heading\n")
        [ errorAt "a-post" 1 "The post has no front matter. Start the file with a '---' line." ]
        "A post starts with front matter"
    }

    test "a file name that is not a slug fails" {
      Expect.equal
        (errors "A Post" (text frontMatter "Body text."))
        [ errorAt "A Post" 1 "Give the file a slug as its name, for example a-post.md." ]
        "The file name is the slug"
    }
  ]
