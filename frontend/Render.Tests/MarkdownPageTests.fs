module TwoPoint.Site.Render.Tests.MarkdownPageTests

open TwoPoint.Site.Render
open TwoPoint.Site.Render.Tests.Fixture

open Expecto

let private parse text = MarkdownPage.parse anyImages Privacy.file text

let private pageText (frontMatter: string) (body: string) = $"---\n{frontMatter}\n---\n\n{body}\n"

let private page body =
  parse (pageText "title: Privacy policy\ndescription: A description" body) |> ok

let private errorAt line message = PostError.at Privacy.file line message

[<Tests>]
let tests =
  testList "Markdown page" [
    test "the page gets the front matter and the body of its file" {
      let page = page "## Who runs the site\n\nRead the [posts](/posts/)."
      Expect.equal page.Title "Privacy policy" "The title"
      Expect.equal page.Description "A description" "The description"
      Expect.equal page.Body.Anchors (set [ "who-runs-the-site" ]) "The heading gives an anchor"
      Expect.equal page.Body.Links [ { Target = Page("/posts/", None); Line = 8 } ] "The internal link has its line"
    }

    test "a missing field, an unknown field, or no front matter fails" {
      Expect.equal
        (parse (pageText "title: Privacy policy" "Text."))
        (Error [ errorAt 1 "The front matter has no 'description' field." ])
        "The description is required"

      Expect.equal
        (parse (pageText "title: Privacy policy\ndescription: A description\ndraft: true" "Text."))
        (Error [ errorAt 4 "Unknown field 'draft'." ])
        "An unknown field fails"

      Expect.equal
        (parse "Text.")
        (Error [ errorAt 1 "The page has no front matter. Start the file with a '---' line." ])
        "The front matter is required"
    }

    test "the embedded privacy page has the heading ids of the privacy file" {
      let privacy = Privacy.parse anyImages |> ok
      Expect.equal privacy.File "frontend/content/privacy.md" "An error gives the path of the privacy file"
      Expect.equal privacy.Title "Privacy policy" "The title"
      Expect.contains privacy.Body.Anchors "browser-storage" "The storage heading"
      Expect.contains privacy.Body.Anchors "embedded-videos" "The embed heading"
    }
  ]
