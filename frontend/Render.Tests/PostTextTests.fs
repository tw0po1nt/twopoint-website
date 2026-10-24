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
