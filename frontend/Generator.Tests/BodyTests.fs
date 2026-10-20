module TwoPoint.Site.Generator.Tests.BodyTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto
open SkiaSharp
open System.Text

let private html body = (published "a-post" (text frontMatter body)).Body.Html

let private bodyErrors body = errors "a-post" (text frontMatter body)

let private blob = "https://twopointwebsite.blob.core.windows.net/posts/a-post"

let private cat = $"{blob}/cat.jpg"

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

    test "a body image keeps its Blob URL, loads lazily, and gets its size" {
      Expect.equal
        (html $"![A cat]({cat})")
        $"<p><img src=\"{cat}\" loading=\"lazy\" width=\"1600\" height=\"900\" alt=\"A cat\" /></p>\n"
        "An image gets loading=\"lazy\", and the width and height of the file"
    }

    test "the size of a WebP image is the size of its file" {
      let webp =
        use bitmap = new SKBitmap(1178, 2556)
        use image = SKImage.FromBitmap bitmap
        use data = image.Encode(SKEncodedImageFormat.Webp, 80)
        data.ToArray()

      let post =
        Post.parse (Images.ofFetch (fetchWith (fun _ -> Ok webp))) (file "a-post") (text frontMatter $"![A demo]({blob}/demo.webp)")

      match post with
      | Ok(Published post) -> Expect.stringContains post.Body.Html "width=\"1178\" height=\"2556\"" "The WebP size"
      | other -> failtest $"%A{other}"
    }

    test "an image on any https host gets its size" {
      Expect.stringContains
        (html "![A cat](https://example.com/cat.jpg)")
        "width=\"1600\" height=\"900\""
        "An https image off Blob gets its size"
    }

    test "an image that is not an https URL fails with its line" {
      Expect.equal
        (bodyErrors "![A](/cat.jpg)\n\n![B](http://twopointwebsite.blob.core.windows.net/cat.jpg)")
        [ errorAt "a-post" 9 "Give the image '/cat.jpg' as an https URL."
          errorAt "a-post" 11 "Give the image 'http://twopointwebsite.blob.core.windows.net/cat.jpg' as an https URL." ]
        "A root-relative image and an http image fail"
    }

    test "an img element in raw HTML fails with its line" {
      let message =
        "Write the image as a Markdown image, for example ![A cat](https://example.com/cat.jpg)."

      Expect.equal
        (bodyErrors $"Text.\n\nA cat: <img src=\"{cat}\">.\n\n<div>\n<IMG SRC=\"{cat}\" />\n</div>")
        [ errorAt "a-post" 11 message; errorAt "a-post" 14 message ]
        "An inline img element and an img element in an HTML block fail"
    }

    test "a body image that does not download fails with the post file and the line" {
      let failing = fetchWith (fun _ -> Error "HTTP 404, after 3 attempts")

      Expect.equal
        (Post.parse (Images.ofFetch failing) (file "a-post") (text frontMatter $"Text.\n\n![A cat]({cat})"))
        (Error [ errorAt "a-post" 11 $"The build cannot read the size of the image {cat}: HTTP 404, after 3 attempts." ])
        "A failed download fails the build"
    }

    test "a body image that is not an image file fails with the post file and the line" {
      let text' = fetchWith (fun _ -> Ok(Encoding.UTF8.GetBytes "<html>Not found</html>"))

      Expect.equal
        (Post.parse (Images.ofFetch text') (file "a-post") (text frontMatter $"![A cat]({cat})"))
        (Error [ errorAt "a-post" 9 $"The build cannot read the size of the image {cat}: the file is not an image." ])
        "A file that is not an image fails the build"
    }

    test "two posts with one body image download it one time with a memoized fetch" {
      let fetch, calls = recording anyImage
      let images = Images.ofFetch (ImageFetch.memoize fetch)

      for slug in [ "a-post"; "b-post" ] do
        Post.parse images (file slug) (text frontMatter $"![A cat]({cat})") |> ignore

      Expect.equal (List.ofSeq calls) [ cat ] "One download"
    }

    test "a heading gets its id" {
      Expect.equal
        (html "## The \"OrDefault\" functions\n\n#### Attempt #1")
        "<h2 id=\"the-ordefault-functions\">The &ldquo;OrDefault&rdquo; functions</h2>\n<h4 id=\"attempt-1\">Attempt #1</h4>\n"
        "Each heading gets the Astro id"
    }

    test "each quote mark is curly, two hyphens give an em dash, and three dots give an ellipsis" {
      Expect.equal
        (html "We'll see C#'s `Option`'s case -- \"[a talk](/posts/a-post/)\". It works...mostly.")
        "<p>We’ll see C#’s <code>Option</code>’s case &mdash; “<a href=\"/posts/a-post/\">a talk</a>”. It works…mostly.</p>\n"
        "A quote mark with no pair curls by its position"

      Expect.equal
        (html "A 'pair', (\"a\"), *\"b*, and the '90s.")
        "<p>A &lsquo;pair&rsquo;, (&ldquo;a&rdquo;), <em>“b</em>, and the ’90s.</p>\n"
        "A quote mark after a space, an opening bracket, or the start of an inline opens, but an apostrophe before a decade closes"
    }

    test "an escaped quote mark stays straight" {
      Expect.equal (html "a 5\\\" screen and \\'x") "<p>a 5&quot; screen and 'x</p>\n" "A backslash keeps the mark straight"
      Expect.equal (html "a \\...") "<p>a ...</p>\n" "A backslash keeps three dots"
    }

    test "a quote mark after a dash or a bracket opens in each literal" {
      Expect.equal (html "a --\"quote") "<p>a &mdash;“quote</p>\n" "After a SmartyPants dash"
      Expect.equal (html "a \\[\"quote") "<p>a [“quote</p>\n" "After an escaped bracket"

      Expect.equal
        (html "a \u2013'b and a \u2014\"c")
        "<p>a \u2013‘b and a \u2014“c</p>\n"
        "After an en dash or an em dash character"
    }

    test "the text of a bare-URL autolink stays as the author wrote it" {
      let url = "https://en.wikipedia.org/wiki/Hitchhiker's_Guide"

      Expect.stringContains (html $"See {url} now.") $">{url}</a>" "The link text keeps its apostrophe"
    }

    test "the text of a heading has the marks of the heading HTML" {
      let post = published "a-post" (text frontMatter "## It's a \"test\"")

      Expect.equal
        (post.Body.Headings |> List.map (fun heading -> heading.Id, heading.Text))
        [ "its-a-test", "It’s a “test”" ]
        "The text has curly marks, and the id has none"
    }

    test "an apostrophe at the start of an elided word closes" {
      Expect.equal
        (html "'tis, 'Tis, 'twas, 'em, 'til, 'cause, and 'tisane")
        "<p>’tis, ’Tis, ’twas, ’em, ’til, ’cause, and ‘tisane</p>\n"
        "Each word in the list closes, and a longer word opens"
    }

    test "a quote mark in code stays straight" {
      Expect.equal
        (html "Use `it's \"x\"...`.\n\n```\nlet c = 'a' // \"x\"...\n```")
        "<p>Use <code>it's &quot;x&quot;...</code>.</p>\n<pre tabindex=\"0\"><code>let c = 'a' // &quot;x&quot;...\n</code></pre>\n"
        "Code keeps its quote marks and dots"
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
