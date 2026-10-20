module TwoPoint.Site.Generator.Tests.HighlightTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto

let private html body = (published "a-post" (text frontMatter body)).Body.Html

let private block fence code = html $"```{fence}\n{code}\n```"

let private highlighted fence (lines: string list) =
  let code = lines |> List.map (fun line -> line + "\n") |> String.concat ""
  $"<pre data-lang=\"{fence}\" tabindex=\"0\"><code>{code}</code></pre>\n"

/// Shiki with the css-variables theme gives the same role to each character of these samples.
let private samples =
  [ "fsharp",
    "let add x = x + 1 // one",
    """<span class="tk-keyword">let</span> add x <span class="tk-keyword">=</span> x <span class="tk-keyword">+</span> <span class="tk-constant">1</span> <span class="tk-comment">// one</span>"""
    "csharp",
    "public int Count() => \"a\".Length;",
    """<span class="tk-keyword">public</span> <span class="tk-keyword">int</span> <span class="tk-function">Count</span>() <span class="tk-keyword">=&gt;</span> <span class="tk-string-expression">&quot;a&quot;</span>.<span class="tk-constant">Length</span>;"""
    "swift",
    "let name: String = \"a\" // b",
    """<span class="tk-keyword">let</span> name: <span class="tk-constant">String</span> <span class="tk-keyword">=</span> <span class="tk-string-expression">&quot;a&quot;</span> <span class="tk-comment">// b</span>"""
    "sh",
    "dotnet new console -n App # make",
    """<span class="tk-function">dotnet</span> <span class="tk-string">new</span> <span class="tk-string">console</span> <span class="tk-string">-n</span> <span class="tk-string">App</span> <span class="tk-comment"># make</span>"""
    "diff",
    "- old",
    """<span class="tk-deleted"><span class="tk-diff-mark">-</span> old</span>"""
    "json",
    "{ \"a\": 1, \"b\": true }",
    """{ <span class="tk-keyword">&quot;a&quot;</span><span class="tk-punctuation">:</span> <span class="tk-constant">1</span><span class="tk-punctuation">,</span> <span class="tk-keyword">&quot;b&quot;</span><span class="tk-punctuation">:</span> <span class="tk-constant">true</span> }"""
    "xml",
    "<Project Sdk=\"Microsoft.NET.Sdk\" />",
    """&lt;<span class="tk-string-expression">Project</span> <span class="tk-function">Sdk</span>=<span class="tk-string-expression">&quot;Microsoft.NET.Sdk&quot;</span> /&gt;"""
    "css",
    ".a > b { color: red; }",
    """<span class="tk-function">.a</span> <span class="tk-keyword">&gt;</span> <span class="tk-string-expression">b</span> { <span class="tk-constant">color</span><span class="tk-keyword">:</span> <span class="tk-constant">red</span>; }""" ]

[<Tests>]
let tests =
  testList "Code blocks" [
    testList "each language gives the role classes of its tokens" [
      for fence, code, expected in samples ->
        test fence { Expect.equal (block fence code) (highlighted fence [ expected ]) $"The {fence} tokens get their roles" }
    ]

    test "a code block escapes its text" {
      Expect.equal
        (block "csharp" "if (a < b && c) { }")
        (highlighted
          "csharp"
          [ """<span class="tk-keyword">if</span> (a <span class="tk-keyword">&lt;</span> b <span class="tk-keyword">&amp;&amp;</span> c) { }""" ])
        "Each <, >, &, and quote is an entity"
    }

    test "a diff mark is a span of its own in the color of its line" {
      Expect.equal
        (block "diff" "- old\n+ new\n  same")
        (highlighted
          "diff"
          [ """<span class="tk-deleted"><span class="tk-diff-mark">-</span> old</span>"""
            """<span class="tk-inserted"><span class="tk-diff-mark">+</span> new</span>"""
            "  same" ])
        "The - and + of each changed line are diff marks"
    }

    test "a block with no language has no label and no colors" {
      Expect.equal (html "```\na < b\n```") "<pre tabindex=\"0\"><code>a &lt; b\n</code></pre>\n" "The block is plain escaped text"
    }

    test "an unknown language fails with its line" {
      Expect.equal
        (errors "a-post" (text frontMatter "Text.\n\n```js\nlet a = 1\n```"))
        [ errorAt
            "a-post"
            11
            "The code block has the unknown language 'js'. Use one of these languages: fsharp, csharp, swift, sh, diff, json, xml, css." ]
        "A fence language must be on the list"
    }
  ]
