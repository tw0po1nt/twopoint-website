module TwoPoint.Site.Generator.Tests.BodyTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto

let private html body = (published "a-post" (text frontMatter body)).Body.Html

let private bodyErrors body = errors "a-post" (text frontMatter body)

[<Tests>]
let tests =
  testList "Body" [
    test "an external link opens in a new tab" {
      Expect.equal
        (html "Read [the docs](https://learn.microsoft.com/dotnet/).")
        "<p>Read <a href=\"https://learn.microsoft.com/dotnet/\" target=\"_blank\" rel=\"noopener noreferrer\">the docs</a>.</p>\n"
        "An https link gets target and rel"

      Expect.stringContains
        (html "See <https://example.com/>.")
        "<a href=\"https://example.com/\" target=\"_blank\" rel=\"noopener noreferrer\">"
        "An autolink gets target and rel"
    }

    test "an internal link and a mail link stay in the same tab" {
      Expect.equal
        (html "Read [the post](/posts/a-post/) or [mail me](mailto:me@example.com).")
        "<p>Read <a href=\"/posts/a-post/\">the post</a> or <a href=\"mailto:me@example.com\">mail me</a>.</p>\n"
        "An internal link and a mail link get no target"
    }

    test "a body image loads lazily" {
      Expect.equal
        (html "![A cat](https://example.com/cat.jpg)")
        "<p><img src=\"https://example.com/cat.jpg\" loading=\"lazy\" alt=\"A cat\" /></p>\n"
        "An image gets loading=\"lazy\""
    }

    test "a heading gets its id" {
      Expect.equal
        (html "## The \"OrDefault\" functions\n\n#### Attempt #1")
        "<h2 id=\"the-ordefault-functions\">The &ldquo;OrDefault&rdquo; functions</h2>\n<h4 id=\"attempt-1\">Attempt #1</h4>\n"
        "Each heading gets the Astro id"
    }

    test "a code block is a plain escaped block" {
      Expect.equal
        (html "```csharp\nif (a < b && c) { }\n```")
        "<pre><code>if (a &lt; b &amp;&amp; c) { }\n</code></pre>\n"
        "A code block has no class and escapes its text"
    }

    test "raw HTML with no class or style stays" {
      Expect.equal (html "It is <u>**1000%**</u>.") "<p>It is <u><strong>1000%</strong></u>.</p>\n" "Raw HTML stays"
    }

    test "a class or a style in raw HTML fails with its line" {
      Expect.equal
        (bodyErrors "Text.\n\nIt is <u class=\"big\">this</u>.\n\n<div>\n<p STYLE=\"color: red\">red</p>\n</div>")
        [ errorAt "a-post" 11 "Delete the 'class' attribute. A post has no class or style in raw HTML."
          errorAt "a-post" 14 "Delete the 'STYLE' attribute. A post has no class or style in raw HTML." ]
        "Each class and style attribute fails"
    }

    test "a data attribute that ends in class is not a class" {
      Expect.isNotEmpty (html "<p data-class=\"x\">Text</p>") "data-class is not class"
    }

    test "the word class or style in an attribute value is not an attribute" {
      Expect.isNotEmpty (html "<abbr title=\"a style guide\">SG</abbr>") "A word in a value passes"

      Expect.equal
        (bodyErrors "<span title=\"a class act\" style=\"color: red\">red</span>")
        [ errorAt "a-post" 9 "Delete the 'style' attribute. A post has no class or style in raw HTML." ]
        "Only the style attribute fails"
    }

    test "a link in raw HTML fails with its line" {
      Expect.equal
        (bodyErrors "Text.\n\nRead <a href=\"https://example.com/\">the docs</a>.")
        [ errorAt "a-post" 11 "Write the link as a Markdown link, for example [the docs](https://example.com/)." ]
        "An inline a element fails"

      Expect.isNonEmpty (bodyErrors "<div>\n<A HREF=\"/posts/b-post/\">b</A>\n</div>") "An a element in an HTML block fails"
    }

    test "a link to twopoint.dev fails with its line" {
      Expect.equal
        (bodyErrors "Text.\n\nRead [the post](https://twopoint.dev/posts/a-post/).")
        [ errorAt "a-post" 11 "Give the link to https://twopoint.dev/posts/a-post/ as a root-relative link, for example /posts/a-post/." ]
        "A link to the own host fails"

      Expect.isNonEmpty (bodyErrors "Read <https://twopoint.dev/>.") "An autolink to the own host fails"
    }

    test "a relative link fails" {
      Expect.equal
        (bodyErrors "Read [the post](a-post/).")
        [ errorAt "a-post" 9 "Give the link to 'a-post/' as a root-relative link that starts with /, or as an http or https URL." ]
        "A link must start with /"
    }

    test "the reading time counts each word, code included, at 200 words a minute" {
      let words count = String.replicate count "word " |> _.Trim()
      let readingTime body = (published "a-post" (text frontMatter body)).Body.ReadingTime

      Expect.equal (readingTime (words 200)) 1 "200 words take 1 minute"
      Expect.equal (readingTime (words 201)) 2 "201 words take 2 minutes"
      Expect.equal (readingTime $"{words 160}\n\n```sh\n{words 60}\n```") 2 "The words of a code block count"
    }
  ]
