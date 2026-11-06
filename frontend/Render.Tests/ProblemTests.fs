module TwoPoint.Site.Render.Tests.ProblemTests

open TwoPoint.Site.Render
open TwoPoint.Site.Render.Tests.Fixture

open Expecto

let private problems result = (problemsOf result).Problems

let private azurite = "http://127.0.0.1:10000/devstoreaccount1/assets/a4dd66d174d2f471"

let private azuriteBodies =
  [ "image", $"Text.\n\n![A cat]({azurite}.jpg)"
    "link", $"Text.\n\nRead [the file]({azurite}.jpg)."
    "autolink", $"Text.\n\nSee <{azurite}.jpg>."
    "PDF", $"Text.\n\n::pdf{{file=\"{azurite}.pdf\"}}" ]

[<Tests>]
let tests =
  testList "Problems" [
    test "a front matter problem is an error on its field" {
      Expect.equal
        (problems (parseIn [ seriesRecord "a-series" ] "a-post" (text $"{frontMatter}\nseries: b-series" "Text.")))
        [ Problem.error (Place.Field("series", 7)) "There's no series with the slug 'b-series'." ]
        "The series field"

      Expect.equal
        (problems (parse "a-post" (text (frontMatter.Replace("author: Matthew Watt\n", "")) "Text.")))
        [ Problem.error (Place.Field("author", 1)) "The front matter is missing the 'author' field." ]
        "A missing field is on the opening line"
    }

    test "a body problem is an error on its body line" {
      Expect.equal
        (problems (parse "a-post" (text frontMatter "Text.\n\n```js\nlet a = 1\n```")))
        [ Problem.error
            (Place.Body 4)
            "Unknown code block language 'js'. Use one of: fsharp, csharp, swift, sh, diff, json, xml, css." ]
        "Body line 1 is the line after the closing ---, so the fence on file line 11 is on body line 4"
    }

    test "a problem of the whole post is an error on the post" {
      Expect.equal
        (problems (parse "a-post" "## A heading\n"))
        [ Problem.error (Place.Post 1) "The post has no front matter. Start the file with a '---' line." ]
        "No front matter"

      Expect.equal
        (problems (parse "A Post" (text frontMatter "Text.")))
        [ Problem.error (Place.Post 1) "Name the file with a slug, like a-post.md." ]
        "A file name that is not a slug"
    }

    test "the build output changes a body line back to its file line" {
      let problems =
        FileProblems.create
          (file "a-post")
          9
          [ Problem.warning (Place.Body 3) "A warning."
            Problem.error (Place.Field("tags", 7)) "An error."
            Problem.error (Place.Post 1) "A post error." ]

      Expect.equal
        (FileProblems.format problems)
        [ errorAt "a-post" 1 "A post error."
          errorAt "a-post" 7 "An error."
          errorAt "a-post" 11 "warning: A warning." ]
        "Each problem has its file line, in line order, and a warning says so"
    }

    test "a file with only warnings passes and keeps its warnings" {
      let warning = FileProblems.create (file "a-post") 9 [ Problem.warning (Place.Body 1) "A warning." ]
      let error = FileProblems.create (file "b-post") 9 [ Problem.error (Place.Body 1) "An error." ]

      Expect.equal (FileProblems.collect [ Ok("a", warning) ]) (Ok([ "a" ], [ warning ])) "A warning does not fail"

      Expect.equal
        (FileProblems.collect [ Ok("a", warning); Error error ])
        (Error [ warning; error ])
        "An error fails, and the failure keeps each warning"

      Expect.equal (FileProblems.collect [ Ok("a", error) ]) (Error [ error ]) "An error fails when the file has a value"
    }

    test "the severity of a problem from a parse decides if the file fails" {
      let warning = Problem.warning (Place.Body 1) "A warning."
      let error = Problem.error (Place.Body 2) "An error."

      Expect.equal
        (FileProblems.ofResult (file "a-post") 9 (Ok("a", [ warning ])))
        (Ok("a", FileProblems.create (file "a-post") 9 [ warning ]))
        "A warning passes and keeps its place"

      Expect.equal
        (FileProblems.ofResult (file "a-post") 9 (Ok("a", [ warning; error ])))
        (Error(FileProblems.create (file "a-post") 9 [ warning; error ]))
        "An error fails, and the failure keeps the warning"
    }

    test "an Azurite URL in the body is an error on staging and production" {
      for stage in [ Stage.Staging; Stage.Production ] do
        for name, body in azuriteBodies do
          match problems (parseOn stage "a-post" (text frontMatter body)) with
          | [ { Severity = Severity.Error; Place = Place.Body 4; Message = message } ] ->
            Expect.stringContains message "is an Azurite URL" $"The {name} on {Stage.name stage}"
          | other -> failtest $"The {name} on {Stage.name stage} gives one error on body line 4: %A{other}"
    }

    test "an Azurite URL in the body gives no problem on local" {
      for name, body in azuriteBodies do
        match parseOn Stage.Local "a-post" (text frontMatter body) with
        | Ok(_, problems) -> Expect.isEmpty problems.Problems $"The {name} on local"
        | Error problems -> failtest $"The {name} on local gives no problem: %A{problems}"
    }

    test "a URL on the Azurite port with a scheme other than http or https is not an Azurite URL" {
      let pdf = "ftp://127.0.0.1:10000/devstoreaccount1/assets/a4dd66d174d2f471.pdf"

      Expect.equal
        (problems (parseOn Stage.Local "a-post" (text frontMatter $"Text.\n\n::pdf{{file=\"{pdf}\"}}")))
        [ Problem.error (Place.Body 4) $"The 'file' of '::pdf' must be an https URL to a .pdf file (got '{pdf}')." ]
        "The PDF needs https"
    }

    test "an http URL that is not on Azurite is still an error on local" {
      Expect.equal
        (errorLines (parseOn Stage.Local "a-post" (text frontMatter "![A cat](http://example.com/cat.jpg)")))
        [ errorAt "a-post" 9 "The image 'http://example.com/cat.jpg' must be an https URL." ]
        "Only Azurite gets the http exception"
    }
  ]
