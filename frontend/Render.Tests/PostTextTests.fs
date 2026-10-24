module TwoPoint.Site.Render.Tests.PostTextTests

open TwoPoint.Site.Render

open Expecto

let private text = "---\ntitle: Old title\nexcerpt: 'An excerpt.'\ntags:\n  - FSharp\n---\n\n## Body\n\nSome text.\n"

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
      let composed = PostText.compose text { Title = "New: title"; Excerpt = "Short."; Body = "New body.\n" }
      let fields = PostText.split composed
      Expect.equal fields { Title = "New: title"; Excerpt = "Short."; Body = "New body.\n" } "The fields"
      Expect.stringContains composed "FSharp" "The tags stay"
    }

    test "compose removes the title and the excerpt when the author clears them" {
      let composed = PostText.compose text { Title = ""; Excerpt = " "; Body = "x" }
      Expect.equal (PostText.split composed) { Title = ""; Excerpt = ""; Body = "x" } "Cleared"
      Expect.stringContains composed "FSharp" "The tags stay"
    }

    test "compose of an empty draft still writes a front matter block that the parser finds" {
      let composed = PostText.compose "" { Title = ""; Excerpt = ""; Body = "" }
      Expect.equal composed "---\n{}\n---\n\n" "An empty block"
      Expect.equal (Post.summary composed) { Title = None; PublishDate = None } "Parsed as front matter"
      Expect.equal (PostText.split composed) { Title = ""; Excerpt = ""; Body = "" } "No fields"
    }

    test "compose of a title with a colon gives a title that the post summary reads" {
      let composed = PostText.compose "" { Title = "Part 1: start"; Excerpt = ""; Body = "" }
      Expect.equal (Post.summary composed).Title (Some "Part 1: start") "The title"
    }

    test "compose repairs broken front matter by starting a new block" {
      let composed = PostText.compose "---\ntitle: [\n---\n" { Title = "Fixed"; Excerpt = ""; Body = "x" }
      Expect.equal (PostText.split composed).Title "Fixed" "The title"
    }
  ]
