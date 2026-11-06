module TwoPoint.Site.Render.Tests.PostTextTests

open TwoPoint.Site.Render

open Expecto
open System

let private text = "---\ntitle: Old title\nexcerpt: 'An excerpt.'\ntags:\n  - FSharp\n---\n\n## Body\n\nSome text.\n"

let private compose (baseText: string) (fields: PostText.Fields) =
  match PostText.compose baseText fields with
  | Ok composed -> composed
  | Error reason -> failtestf "Expected a text, got the reason '%s'" reason

[<Tests>]
let tests =
  testList "Post text" [
    test "split gives the title, the excerpt, and the body" {
      let fields = PostText.split text
      Expect.equal fields { Title = "Old title"; Excerpt = "An excerpt."; Body = "## Body\n\nSome text.\n" } "The fields"
    }

    test "split of text with no front matter gives the text as the body" {
      Expect.equal (PostText.split "Only a body.") { Title = ""; Excerpt = ""; Body = "Only a body." } "The body"
    }

    test "compose changes the title, the excerpt, and the body, and keeps the other fields" {
      let composed = compose text { Title = "New: title"; Excerpt = "Short."; Body = "New body.\n" }
      let fields = PostText.split composed
      Expect.equal fields { Title = "New: title"; Excerpt = "Short."; Body = "New body.\n" } "The fields"
      Expect.stringContains composed "FSharp" "The tags stay"
    }

    test "compose removes the title and the excerpt when the author clears them" {
      let composed = compose text { Title = ""; Excerpt = " "; Body = "x" }
      Expect.equal (PostText.split composed) { Title = ""; Excerpt = ""; Body = "x" } "Cleared"
      Expect.stringContains composed "FSharp" "The tags stay"
    }

    test "compose of an empty draft still writes a front matter block that the parser finds" {
      let composed = compose "" { Title = ""; Excerpt = ""; Body = "" }
      Expect.equal composed "---\n{}\n---\n\n" "An empty block"
      Expect.equal (Post.summary composed) { Title = None; PublishDate = None } "Parsed as front matter"
      Expect.equal (PostText.split composed) { Title = ""; Excerpt = ""; Body = "" } "No fields"
    }

    test "compose of a title with a colon gives a title that the post summary reads" {
      let composed = compose "" { Title = "Part 1: start"; Excerpt = ""; Body = "" }
      Expect.equal (Post.summary composed).Title (Some "Part 1: start") "The title"
    }

    test "compose refuses front matter that the YAML parser cannot read, and gives the reason" {
      match PostText.compose "---\ntitle: [\ntags:\n  - FSharp\n---\n" { Title = "Fixed"; Excerpt = ""; Body = "x" } with
      | Error reason -> Expect.isNotEmpty reason "A reason"
      | Ok composed -> failtestf "Expected a refusal, got %s" composed
    }

    test "compose refuses front matter that is not a YAML mapping" {
      match PostText.compose "---\n- one\n- two\n---\n" { Title = "x"; Excerpt = ""; Body = "" } with
      | Error reason -> Expect.equal reason "The front matter must be a single YAML mapping." "The reason"
      | Ok composed -> failtestf "Expected a refusal, got %s" composed
    }
  ]

let private detailed =
  "---\ntitle: A\nauthor: Matt\ncategory: Dev\ntags:\n  - FSharp\nseries: my-series\nimage: https://x.dev/a.png\nshowOnHomepage: false\n---\n\nBody.\n"

let private composeWith (baseText: string) (details: PostText.Details) =
  match PostText.composeWithDetails baseText { Title = "A"; Excerpt = ""; Body = "Body.\n" } details with
  | Ok composed -> composed
  | Error reason -> failtestf "Expected a text, got the reason '%s'" reason

[<Tests>]
let detailsTests =
  testList "Post details" [
    test "details reads the author, the category, the tags, the series, the image, and the home page flag" {
      Expect.equal
        (PostText.details detailed)
        { Author = Some "Matt"
          Category = Some "Dev"
          Tags = [ "FSharp" ]
          Series = Some "my-series"
          Image = Some "https://x.dev/a.png"
          ShowOnHomepage = false }
        "The details"
    }

    test "details of a post with no such fields gives no details and shows the post on the home page" {
      Expect.equal
        (PostText.details "---\ntitle: A\n---\n")
        { Author = None; Category = None; Tags = []; Series = None; Image = None; ShowOnHomepage = true }
        "No details"
    }

    test "details of front matter that the YAML parser cannot read gives no details" {
      Expect.equal (PostText.details "---\ntitle: [\n---\n").Tags [] "No tags"
    }

    test "composeWithDetails with the stored details changes no field" {
      Expect.equal (composeWith detailed (PostText.details detailed)) (compose detailed (PostText.split detailed)) "Same text"
    }

    test "composeWithDetails writes a new category, new tags, a series, and an image" {
      let details =
        { PostText.details "---\ntitle: A\n---\n" with
            Category = Some "Series"
            Tags = [ "FSharp"; "Functional programming" ]
            Series = Some "my-series"
            Image = Some "https://x.dev/a.png" }

      let composed = composeWith "---\ntitle: A\n---\n" details
      Expect.equal (PostText.details composed) details "Read back"
    }

    test "composeWithDetails removes a field that the author clears" {
      let details = { PostText.details detailed with Category = None; Tags = []; Series = None; Image = None }
      let composed = composeWith detailed details
      Expect.equal (PostText.details composed) details "Read back"
      Expect.isFalse (composed.Contains "category") "No category field"
      Expect.isFalse (composed.Contains "tags") "No tags field"
    }

    test "composeWithDetails writes showOnHomepage only when the post is not on the home page" {
      let shown = composeWith detailed { PostText.details detailed with ShowOnHomepage = true }
      Expect.isFalse (shown.Contains "showOnHomepage") "The field goes"
      let hidden = composeWith "---\ntitle: A\n---\n" { PostText.details "---\n---\n" with ShowOnHomepage = false }
      Expect.stringContains hidden "showOnHomepage: false" "The field"
    }

    test "composeWithDetails keeps the other fields" {
      let composed =
        composeWith
          "---\ntitle: A\npublishDate: 2026-01-01T00:00:00Z\n---\n"
          { PostText.details "---\n---\n" with Author = Some "Matt" }

      Expect.stringContains composed "publishDate: 2026-01-01T00:00:00Z" "The publish date stays"
    }

    test "composeWithDetails refuses front matter that the YAML parser cannot read" {
      match PostText.composeWithDetails "---\ntitle: [\n---\n" { Title = "x"; Excerpt = ""; Body = "" } (PostText.details "") with
      | Error reason -> Expect.isNotEmpty reason "A reason"
      | Ok composed -> failtestf "Expected a refusal, got %s" composed
    }

    test "withPublishDate sets the publish date and keeps the other fields and the body" {
      let date = System.DateTimeOffset(2026, 11, 2, 13, 0, 0, System.TimeSpan.Zero)

      match PostText.withPublishDate text date with
      | Ok dated ->
        Expect.equal (Post.summary dated).PublishDate (Some date) "The publish date"
        Expect.equal (PostText.split dated) (PostText.split text) "The fields and the body"
        Expect.stringContains dated "FSharp" "The tags stay"
      | Error reason -> failtestf "Expected a text, got the reason '%s'" reason
    }

    test "withPublishDate replaces a publish date that the text has" {
      let dated = "---\npublishDate: 2020-01-01T00:00:00Z\ntitle: T\n---\n\nBody.\n"
      let date = System.DateTimeOffset(2026, 11, 2, 13, 0, 0, System.TimeSpan.Zero)

      match PostText.withPublishDate dated date with
      | Ok replaced -> Expect.equal (Post.summary replaced).PublishDate (Some date) "The new date"
      | Error reason -> failtestf "Expected a text, got the reason '%s'" reason
    }

    test "withUpdateDate sets the update date and keeps the publish date, the fields, and the body" {
      let dated = "---\npublishDate: 2020-01-01T00:00:00Z\nupdateDate: 2021-01-01T00:00:00Z\ntitle: T\n---\n\nBody.\n"
      let date = System.DateTimeOffset(2026, 11, 2, 13, 0, 0, System.TimeSpan.Zero)

      match PostText.withUpdateDate dated date with
      | Ok updated ->
        Expect.stringContains updated "updateDate: 2026-11-02T13:00:00Z" "The new date"
        Expect.stringContains updated "publishDate: 2020-01-01T00:00:00Z" "The publish date stays"
        Expect.equal (PostText.split updated) (PostText.split dated) "The fields and the body"
      | Error reason -> failtestf "Expected a text, got the reason '%s'" reason
    }

    test "withoutPublishDate removes the publish date and keeps the other fields and the body" {
      let dated = "---\npublishDate: 2020-01-01T00:00:00Z\ntitle: T\nauthor: A\n---\n\nBody.\n"

      match PostText.withoutPublishDate dated with
      | Ok undated ->
        Expect.equal (Post.summary undated).PublishDate None "No publish date"
        Expect.stringContains undated "author: A" "The author stays"
        Expect.equal (PostText.split undated) (PostText.split dated) "The fields and the body"
      | Error reason -> failtestf "Expected a text, got the reason '%s'" reason
    }
  ]

let private roundTrip (series: Series) =
  FrontMatter.parseSeries $"{series.Slug}.yml" (PostText.seriesRecord series)

[<Tests>]
let seriesRecordTests =
  testList "Series record text" [
    test "a series record reads back to the same series" {
      let series =
        { Slug = "footguns"
          Title = "Matt's Footgun Collection: #1"
          Excerpt = "- 'Quoted', \"double\", yes, and null"
          Image = Some(Uri "https://twopointwebsite.blob.core.windows.net/assets/823d1b141eaed748.jpg") }

      Expect.equal (roundTrip series) (Ok series) "Each field reads back"
    }

    test "a series record with no image has no image field" {
      let series = { Slug = "notes"; Title = "Notes"; Excerpt = "Short notes"; Image = None }

      Expect.equal (roundTrip series) (Ok series) "The series reads back"
      Expect.isFalse ((PostText.seriesRecord series).Contains "image") "No image field"
    }
  ]
