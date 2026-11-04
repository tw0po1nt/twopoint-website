module TwoPoint.Site.Render.Tests.CommentBodyTests

open TwoPoint.Site.Render

open Expecto

let private html source = CommentBody.html Stage.Production source

let private external url text =
  $"<a href=\"{url}\" target=\"_blank\" rel=\"nofollow ugc noopener noreferrer\">{text}</a>"

let private paragraph inner = $"<p>{inner}</p>\n"

let private renders (cases: (string * string * string) list) =
  cases
  |> List.map (fun (name, source, expected) -> test name { Expect.equal (html source) expected "The HTML of the comment" })

let private allowed =
  testList "an allowed element renders as HTML" (
    renders
      [ "emphasis", "an *important* word", paragraph "an <em>important</em> word"
        "strong", "a **very** important word", paragraph "a <strong>very</strong> important word"
        "inline code", "use `List.map` here", paragraph "use <code>List.map</code> here"
        "a link", "[the docs](https://learn.microsoft.com/)", paragraph (external "https://learn.microsoft.com/" "the docs")
        "an autolink", "<https://example.com/>", paragraph (external "https://example.com/" "https://example.com/")
        "a bulleted list", "- one\n- two", "<ul>\n<li>one</li>\n<li>two</li>\n</ul>\n"
        "a numbered list", "1. one\n2. two", "<ol>\n<li>one</li>\n<li>two</li>\n</ol>\n"
        "a block quote", "> a quote", "<blockquote>\n<p>a quote</p>\n</blockquote>\n" ]
  )

let private asText =
  testList "other syntax stays as text" (
    renders
      [ "a heading", "# Title", paragraph "# Title"
        "a setext heading", "Title\n---", paragraph "Title<br />\n---"
        "a rule", "one\n\n---\n\ntwo", "<p>one</p>\n<p>---</p>\n<p>two</p>\n"
        "a rule of stars after a line", "one\n***", paragraph "one<br />\n***"
        "a table", "| a | b |\n|---|---|", paragraph "| a | b |<br />\n|---|---|"
        "inline HTML", "a <b>bold</b> word", paragraph "a &lt;b&gt;bold&lt;/b&gt; word"
        "an HTML block", "<script>alert(1)</script>", paragraph "&lt;script&gt;alert(1)&lt;/script&gt;"
        "strikethrough", "~~gone~~", paragraph "~~gone~~" ]
  )

let private images =
  testList "an image becomes a link" (
    renders
      [ "an image links to its URL with its alt text", "![A cat](https://example.com/cat.jpg)", paragraph (external "https://example.com/cat.jpg" "A cat")
        "an image in a link is its alt text", "[![A cat](https://example.com/cat.jpg)](https://example.com/)", paragraph (external "https://example.com/" "A cat")
        "an image with a refused URL is its alt text", "![A cat](javascript:alert(1))", paragraph "A cat" ]
  )

let private urls =
  testList "only an absolute http, https, or mailto URL makes a link" (
    renders
      [ "http", "[a](http://example.com/)", paragraph (external "http://example.com/" "a")
        "https", "[a](https://example.com/)", paragraph (external "https://example.com/" "a")
        "mailto", "[mail me](mailto:me@example.com)", paragraph "<a href=\"mailto:me@example.com\">mail me</a>"
        "an email autolink", "<me@example.com>", paragraph "<a href=\"mailto:me@example.com\">me@example.com</a>"
        "javascript", "[a](javascript:alert(1))", paragraph "a"
        "javascript in capitals", "[a](JAVASCRIPT:alert(1))", paragraph "a"
        "a javascript autolink", "<javascript:alert(1)>", paragraph "javascript:alert(1)"
        "data", "[a](data:text/html,hi)", paragraph "a"
        "relative", "[a](/posts/a-post/)", paragraph "a"
        "fragment", "[a](#setup)", paragraph "a"
        "a reference to javascript", "[a][r]\n\n[r]: javascript:alert(1)", paragraph "a"
        "the text of a refused link keeps its marks", "[an *important* link](/a)", paragraph "an <em>important</em> link" ]
  )

let private bareUrls =
  testList "a bare URL" (
    renders
      [ "with https is a link", "see https://example.com/a", paragraph $"""see {external "https://example.com/a" "https://example.com/a"}"""
        "with http is a link", "see http://example.com/a", paragraph $"""see {external "http://example.com/a" "http://example.com/a"}"""
        "of a www host stays as text", "see www.example.com", paragraph "see www.example.com"
        "of an email address stays as text", "mail me@example.com", paragraph "mail me@example.com"
        "with mailto stays as text", "mail mailto:me@example.com", paragraph "mail mailto:me@example.com" ]
  )

let private targets =
  testList "the target and rel of a link" (
    renders
      [ "an external link opens in a new tab with nofollow ugc", "[a](https://example.com/)", paragraph (external "https://example.com/" "a")
        "a link to twopoint.dev opens in the same tab with no rel", "[a](https://twopoint.dev/posts/a-post/)", paragraph "<a href=\"https://twopoint.dev/posts/a-post/\">a</a>"
        "a link to www.twopoint.dev opens in the same tab", "https://www.twopoint.dev/", paragraph "<a href=\"https://www.twopoint.dev/\">https://www.twopoint.dev/</a>"
        "a mailto link has no target", "[a](mailto:me@example.com)", paragraph "<a href=\"mailto:me@example.com\">a</a>" ]
  )

let private stages =
  testList "an Azurite URL" [
    test "is a link only on the local stage" {
      let source = "[a](http://127.0.0.1:10000/devstoreaccount1/assets/a.png)"

      Expect.equal
        (CommentBody.html Stage.Local source)
        (paragraph (external "http://127.0.0.1:10000/devstoreaccount1/assets/a.png" "a"))
        "The local stage links to Azurite"

      Expect.equal (CommentBody.html Stage.Staging source) (paragraph "a") "The staging stage refuses Azurite"
    }
  ]

let private code =
  testList "a code block renders as plain code" (
    renders
      [ "a fence of a known language", "```fsharp\nlet x = 1 < 2\n```", "<pre tabindex=\"0\"><code>let x = 1 &lt; 2\n</code></pre>\n"
        "a fence of an unknown language", "```cobol\nDISPLAY 'HI'.\n```", "<pre tabindex=\"0\"><code>DISPLAY 'HI'.\n</code></pre>\n"
        "an indented block", "    let x = 1\n    let y = 2", "<pre tabindex=\"0\"><code>let x = 1\nlet y = 2\n</code></pre>\n" ]
  )

let private lineBreaks =
  testList "line breaks" (
    renders
      [ "one newline is a line break", "one\ntwo", paragraph "one<br />\ntwo"
        "a blank line starts a new paragraph", "one\n\ntwo", "<p>one</p>\n<p>two</p>\n" ]
  )

let private live =
  testList "the live comments of 2026-11-01" (
    renders
      [ "the record struct code becomes inline code",
        "Thanks for unpacking the baggage. It appreciate how you show how to avoid Nulls using Maybe and Just. It was good to be remined of the difference between using the stack and the heap. I had thought record was on the stack, but you pointed out it's the heap. `record struct` puts it on the stack, good to know!",
        paragraph "Thanks for unpacking the baggage. It appreciate how you show how to avoid Nulls using Maybe and Just. It was good to be remined of the difference between using the stack and the heap. I had thought record was on the stack, but you pointed out it's the heap. <code>record struct</code> puts it on the stack, good to know!"
        "the blank line starts a paragraph and the bare URL becomes a link",
        "Thanks for sharing this. I can tell you have thought deeply about FP and DDD and that you have experienced developing in other approaches. I appreciated your code samples and patiently explaining what things are. Keep them coming.\n\nAlso, great job in the .Net Conf 2025 talk! (https://youtu.be/GcEHiY6Vp-8)",
        $"""<p>Thanks for sharing this. I can tell you have thought deeply about FP and DDD and that you have experienced developing in other approaches. I appreciated your code samples and patiently explaining what things are. Keep them coming.</p>
<p>Also, great job in the .Net Conf 2025 talk! ({external "https://youtu.be/GcEHiY6Vp-8" "https://youtu.be/GcEHiY6Vp-8"})</p>
"""
        "the comment with no syntax does not change",
        "WOW! I have learned two things from reading this. One - I have a lot to learn about coding. Two - I’m so glad there are people like you who are brilliant and do all of this so people like me can enjoy technology!",
        paragraph "WOW! I have learned two things from reading this. One - I have a lot to learn about coding. Two - I’m so glad there are people like you who are brilliant and do all of this so people like me can enjoy technology!" ]
  )

[<Tests>]
let tests =
  testList "CommentBody" [ allowed; asText; images; urls; bareUrls; targets; stages; code; lineBreaks; live ]
