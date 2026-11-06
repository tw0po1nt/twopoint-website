module TwoPoint.Site.Render.Tests.MarkdownPageTests

open TwoPoint.Site.Render
open TwoPoint.Site.Render.Tests.Fixture

open Expecto

let private parse text =
  MarkdownPage.parse anyImages Stage.Production Privacy.file text

let private page body =
  parse (text "title: Privacy policy\ndescription: A description" body) |> ok |> fst

let private errorAt line message = $"{Privacy.file}:{line}: {message}"

[<Tests>]
let tests =
  testList "Markdown page" [
    test "the page gets the front matter and the body of its file" {
      let page = page "## Who runs the site\n\nRead the [posts](/posts/)."
      Expect.equal page.Title "Privacy policy" "The title"
      Expect.equal page.Description "A description" "The description"
      Expect.equal page.Body.Anchors (set [ "who-runs-the-site" ]) "The heading gives an anchor"
      Expect.equal page.Body.Links [ { Target = Page("/posts/", None); Line = 4 } ] "The internal link has its body line"
    }

    test "a missing field, an unknown field, or no front matter fails" {
      Expect.equal
        (errorLines (parse (text "title: Privacy policy" "Text.")))
        ([ errorAt 1 "The front matter is missing the 'description' field." ])
        "The description is required"

      Expect.equal
        (errorLines (parse (text "title: Privacy policy\ndescription: A description\ndraft: true" "Text.")))
        ([ errorAt 4 "Unknown field 'draft'." ])
        "An unknown field fails"

      Expect.equal
        (errorLines (parse "Text."))
        ([ errorAt 1 "The page has no front matter. Start the file with a '---' line." ])
        "The front matter is required"
    }

    test "the embedded privacy page has the heading ids of the privacy file" {
      let privacy = Privacy.parse anyImages Stage.Production |> ok |> fst
      Expect.equal privacy.File "frontend/content/privacy.md" "An error gives the path of the privacy file"
      Expect.equal privacy.Title "Privacy policy" "The title"
      Expect.contains privacy.Body.Anchors "browser-storage" "The storage heading"
      Expect.contains privacy.Body.Anchors "embedded-videos" "The embed heading"
    }
  ]
