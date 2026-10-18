module TwoPoint.Site.Generator.Tests.ClassCheckTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto
open System.Text

let private text (map: RenderMap) path = Encoding.UTF8.GetString(map[SiteFile path] ())

let private pages (map: RenderMap) =
  RenderMap.sitePaths map |> Seq.filter _.EndsWith(".html") |> Seq.map (text map)

/// Posts that give each part of the post page: a talk, a post with no series and no tags, and posts with older and newer posts.
let private fixturePosts =
  [ postWith "a-talk" "2025-01-01T00:00:00Z" "Talks" [ "FSharp" ] None
    postWith "a-one-off" "2025-06-01T00:00:00Z" "One-offs" [] None
    postWith "a-series-post" "2025-09-01T00:00:00Z" "Series" [ "FSharp" ] (Some "fable-reminders") ]

[<Tests>]
let tests =
  testList "Class check" [
    test "each class of the real output and the fixture output has a rule, and each rule has a use" {
      let real = RenderMap.create Stage.Production realContent.Value
      let fixture = RenderMap.create Stage.Production (content fixturePosts)
      let css = text real (realContent.Value.Assets.Stylesheet.TrimStart '/')
      Expect.isEmpty (ClassCheck.check css (Seq.append (pages real) (pages fixture))) "The site CSS agrees with the output"
    }

    test "a class with no rule fails" {
      Expect.equal
        (ClassCheck.check ".post { margin: 0; }" [ """<p class="post excerpt">Text.</p>""" ])
        [ "The class 'excerpt' has no CSS rule." ]
        "The excerpt class has no rule"
    }

    test "a rule with no use fails" {
      Expect.equal
        (ClassCheck.check
          "@media (max-width: 720px) { .post, .card:hover { margin: 0; } }"
          [ """<p class="post">Text.</p>""" ])
        [ "The CSS rule for '.card' has no use." ]
        "The card rule has no use"
    }

    test "a class in a comment, a string, or a declaration is not a rule" {
      Expect.isEmpty
        (ClassCheck.check
          """/* .old */ .post::before { content: '.quoted'; background: url("/_/a.woff2"); }"""
          [ """<p class="post">Text.</p>""" ])
        "Only a selector gives a rule"
    }

    test "an unused rule for a code token passes" {
      Expect.isEmpty
        (ClassCheck.check ".post, .tk-keyword { margin: 0; }" [ """<p class="post">Text.</p>""" ])
        "A post can omit a token role"
    }
  ]
