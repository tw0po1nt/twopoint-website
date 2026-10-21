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
         series: matts-footgun-collection\n\
         image: https://example.com/post.jpg\n\
         showOnHomepage: false"

      let series =
        { Slug = "matts-footgun-collection"
          Title = "Matt's Footgun Collection"
          Excerpt = "Footguns"
          Image = Some(Uri "https://example.com/series.jpg") }

      let post = publishedIn [ seriesRecord "a-series"; series ] "a-post" (withField fields)

      Expect.equal
        post.FrontMatter
        { PublishDate = DateTimeOffset(2026, 2, 4, 13, 0, 0, TimeSpan.Zero)
          UpdateDate = Some(DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.Zero))
          Title = "A post"
          Excerpt = "An excerpt"
          Author = "Matthew Watt"
          Category = { Slug = "one-offs"; Title = "One-offs" }
          Tags =
            [ { Slug = "csharp"; Title = "C#" }
              { Slug = "net"; Title = ".NET" }
              { Slug = "2026"; Title = "2026" } ]
          Series = Some series
          Image = Some(Uri "https://example.com/post.jpg")
          ShowOnHomepage = false }
        "The post has each field"

      Expect.equal post.Slug "a-post" "The slug is the file name"
    }

    test "an optional field takes its default" {
      let frontMatter = (published "a-post" (text frontMatter "Body text.")).FrontMatter
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
      for field in [ "showHeroImage: true"; "metadata: {}"; "draft: true"; "draft: false" ] do
        Expect.isNonEmpty (errors "a-post" (withField field)) $"'{field}' is not a field"
    }

    test "an unknown series slug fails with its line" {
      Expect.equal
        (errorsIn [ seriesRecord "a-series" ] "a-post" (withField "series: b-series"))
        [ errorAt "a-post" 7 "No series has the slug 'b-series'." ]
        "The series slug must have a series record"
    }

    test "a copy of the series fields fails" {
      Expect.equal
        (errorsIn [ seriesRecord "a-series" ] "a-post" (withField "series:\n  slug: a-series\n  title: A series\n  excerpt: Posts"))
        [ errorAt "a-post" 8 "Give 'series' as text." ]
        "A post refers to its series by the slug only"
    }

    test "each missing required field fails" {
      for field in [ "publishDate"; "author"; "title"; "excerpt"; "category" ] do
        Expect.equal
          (errors "a-post" (without field))
          [ errorAt "a-post" 1 $"The front matter has no '{field}' field." ]
          $"{field} is required"
    }

    test "a bad value fails with its line" {
      let cases =
        [ "showOnHomepage: yes", "Give 'showOnHomepage' as true or false."
          "updateDate: 2026-02-04T13:00:00+02:00", "Give 'updateDate' as an ISO 8601 UTC time, for example 2026-02-04T13:00:00Z."
          "updateDate: soon", "Give 'updateDate' as an ISO 8601 UTC time, for example 2026-02-04T13:00:00Z."
          "image: http://example.com/a.jpg", "Give 'image' as an https URL."
          "tags: CSharp", "Give 'tags' as a list."
          "series: [a-series]", "Give 'series' as text." ]

      for field, message in cases do
        Expect.equal (errors "a-post" (withField field)) [ errorAt "a-post" 7 message ] $"'{field}' fails"
    }

    test "each bad field of a post fails" {
      let fields = frontMatter.Replace("author: Matthew Watt\n", "") + "\nshowOnHomepage: maybe"

      Expect.equal
        (errors "a-post" (text fields "Body text."))
        [ errorAt "a-post" 1 "The front matter has no 'author' field."
          errorAt "a-post" 6 "Give 'showOnHomepage' as true or false." ]
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

let private seriesFile slug =
  PostFolder.filePath PostFolder.pulled Container.Series $"{slug}.yml"

let private seriesErrors slug yaml =
  match FrontMatter.parseSeries (seriesFile slug) yaml with
  | Error errors -> errors
  | Ok series -> failtest $"The series record has no error: %A{series}"

let private seriesErrorAt slug line message = PostError.at (seriesFile slug) line message

[<Tests>]
let seriesTests =
  testList "Series record" [
    test "a series record reads each field, and its file name gives the slug" {
      Expect.equal
        (FrontMatter.parseSeries
          (seriesFile "fable-reminders")
          "title: 'Fable Reminders'\nexcerpt: Notes on Fable\nimage: https://example.com/fable.jpg\n")
        (Ok
          { Slug = "fable-reminders"
            Title = "Fable Reminders"
            Excerpt = "Notes on Fable"
            Image = Some(Uri "https://example.com/fable.jpg") })
        "The series has each field"
    }

    test "a series record has no image by default" {
      Expect.equal
        (FrontMatter.parseSeries (seriesFile "a-series") "title: A series\nexcerpt: Posts\n" |> Result.map _.Image)
        (Ok None)
        "The image is optional"
    }

    test "each missing required field of a series record fails at line 1" {
      Expect.equal
        (seriesErrors "a-series" "image: https://example.com/a.jpg\n")
        [ seriesErrorAt "a-series" 1 "The series record has no 'title' field."
          seriesErrorAt "a-series" 1 "The series record has no 'excerpt' field." ]
        "The title and the excerpt are required"
    }

    test "a series record that is not a mapping fails" {
      Expect.equal
        (seriesErrors "a-series" "- A series
")
        [ seriesErrorAt "a-series" 1 "Give the series record as a YAML mapping." ]
        "A series record is a YAML mapping"
    }

    test "an unknown or bad field of a series record fails with its line" {
      Expect.equal
        (seriesErrors "a-series" "title: A series\nslug: a-series\nexcerpt: Posts\nimage: http://example.com/a.jpg\n")
        [ seriesErrorAt "a-series" 2 "Unknown field 'slug'." ]
        "The file name gives the slug"

      Expect.equal
        (seriesErrors "a-series" "title: A series\nexcerpt: Posts\nimage: http://example.com/a.jpg\n")
        [ seriesErrorAt "a-series" 3 "Give 'image' as an https URL." ]
        "The image is an https URL"
    }

    test "a series file name that is not a slug fails" {
      Expect.equal
        (seriesErrors "A Series" "title: A series\nexcerpt: Posts\n")
        [ seriesErrorAt "A Series" 1 "Give the file a slug as its name, for example a-series.yml." ]
        "The file name is the slug"
    }
  ]
