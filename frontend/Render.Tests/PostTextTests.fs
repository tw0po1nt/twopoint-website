module TwoPoint.Site.Render.Tests.PostTextTests

open TwoPoint.Site.Render

open Expecto

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
      | Error reason -> Expect.equal reason "The front matter is not a YAML mapping." "The reason"
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
  ]
