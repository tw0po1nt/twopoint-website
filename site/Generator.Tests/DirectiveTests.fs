module TwoPoint.Site.Generator.Tests.DirectiveTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto

let private html body = (published "a-post" (text frontMatter body)).Body.Html

/// The directive is on line 11 of the fixture post.
let private directiveError line =
  match errors "a-post" (text frontMatter $"Text.\n\n{line}\n\nMore text.") with
  | [ error ] ->
    Expect.equal (error.File, error.Line) (file "a-post", 11) "The error gives the file and the line of the directive"
    error.Message
  | errors -> failtest $"The directive gives one error: %A{errors}"

let private slides =
  "https://twopointwebsite.blob.core.windows.net/posts/talks/functional-programming-more-than-just-a-coding-style/slides.pdf"

[<Tests>]
let tests =
  testList "Directives" [
    test "::youtube gives a youtube-nocookie.com iframe" {
      Expect.equal
        (html "::youtube{id=\"qRuKTkDvAk4\"}")
        "<iframe src=\"https://www.youtube-nocookie.com/embed/qRuKTkDvAk4\" title=\"YouTube video player\" \
         allow=\"accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture\" \
         referrerpolicy=\"strict-origin-when-cross-origin\" allowfullscreen loading=\"lazy\"></iframe>\n"
        "The video plays from youtube-nocookie.com"
    }

    test "::pdf gives a figure with the static download link" {
      Expect.equal
        (html $"#### Slides\n\n::pdf{{file=\"{slides}\"}}")
        $"<h4 id=\"slides\">Slides</h4>\n<figure><a href=\"{slides}\" target=\"_blank\" rel=\"noopener noreferrer\" download>Download</a></figure>\n"
        "The download link works with no script"
    }

    test "a directive line can interrupt a paragraph" {
      Expect.stringContains (html "Text.\n::youtube{id=\"qRuKTkDvAk4\"}") "<p>Text.</p>\n<iframe" "The directive is its own block"
    }

    test "a :: line in a code block or in a paragraph is text" {
      Expect.stringContains (html "```\n::youtube{id=\"x\"}\n```") "<code>::youtube{id=&quot;x&quot;}" "A code block keeps the line"
      Expect.equal (html "A list :: of items.") "<p>A list :: of items.</p>\n" "A :: in a line is text"
    }

    test "an unknown name fails" {
      Expect.equal
        (directiveError "::vimeo{id=\"123\"}")
        "The directive '::vimeo' is unknown. Use one of these names: pdf, youtube."
        "Only pdf and youtube are directives"
    }

    test "a missing attribute fails" {
      Expect.equal (directiveError "::youtube{}") "The directive '::youtube' has no 'id' attribute." "youtube needs id"
      Expect.equal (directiveError "::pdf{}") "The directive '::pdf' has no 'file' attribute." "pdf needs file"
    }

    test "an unknown attribute fails" {
      Expect.equal
        (directiveError "::youtube{id=\"qRuKTkDvAk4\" start=\"10\"}")
        "The directive '::youtube' has the unknown attribute 'start'. Use only 'id'."
        "youtube has only id"

      Expect.equal
        (directiveError $"::pdf{{file=\"{slides}\" title=\"Slides\"}}")
        "The directive '::pdf' has the unknown attribute 'title'. Use only 'file'."
        "pdf has only file"
    }

    test "an attribute two times fails" {
      Expect.equal
        (directiveError "::youtube{id=\"qRuKTkDvAk4\" id=\"GcEHiY6Vp-8\"}")
        "The directive '::youtube' has the attribute 'id' more than one time."
        "Each attribute occurs one time"
    }

    test "a bad YouTube id fails" {
      for id in [ ""; "qRuKTkDvAk"; "qRuKTkDvAk4x"; "qRuKTkDvAk!" ] do
        Expect.equal
          (directiveError $"::youtube{{id=\"{id}\"}}")
          $"The 'id' of '::youtube' is '{id}'. Give a YouTube video id of 11 letters, digits, '-', or '_'."
          $"'{id}' is a bad id"
    }

    test "a bad PDF file fails" {
      for file in
        [ "http://example.com/slides.pdf"
          "https://example.com/slides.pptx"
          "/slides.pdf"
          "slides.pdf" ] do
        Expect.equal
          (directiveError $"::pdf{{file=\"{file}\"}}")
          $"The 'file' of '::pdf' is '{file}'. Give the https URL of a .pdf file."
          $"'{file}' is a bad file"
    }

    test "a directive with a bad shape fails" {
      let message =
        "Write the directive as ::name{attribute=\"value\"}, alone on its line. Use one of these names: pdf, youtube."

      for line in
        [ "::youtube"
          "::youtube{id=\"qRuKTkDvAk4\"} and text"
          "::youtube[Video]{id=\"qRuKTkDvAk4\"}"
          ":::youtube{id=\"qRuKTkDvAk4\"}" ] do
        Expect.equal (directiveError line) message $"'{line}' is bad"
    }

    test "an attribute with no quotes fails" {
      Expect.equal
        (directiveError "::youtube{id=qRuKTkDvAk4}")
        "Give each attribute of '::youtube' as name=\"value\"."
        "A value has quotes"
    }
  ]
