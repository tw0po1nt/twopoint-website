module TwoPoint.Site.Generator.Tests.SlugTests

open TwoPoint.Site.Generator

open Expecto

[<Tests>]
let tests =
  testList "Slug" [
    test "a heading id drops punctuation and joins the words with hyphens" {
      Expect.equal
        (Slug.headingIds [ "“OrDefault” functions in System.Linq.Enumerable"; "Attempt #1"; "The Footgun: types" ])
        [ "ordefault-functions-in-systemlinqenumerable"; "attempt-1"; "the-footgun-types" ]
        "The ids follow the github-slugger rules"
    }

    test "a repeated heading id gets a number" {
      Expect.equal
        (Slug.headingIds [ "Setup"; "Setup"; "Setup-1"; "Setup" ])
        [ "setup"; "setup-1"; "setup-1-1"; "setup-2" ]
        "The ids follow the github-slugger rules for a repeated id"
    }

    test "a term slug gives the Astro value of each current tag and category" {
      let values =
        [ ".NET", "net"
          ".NET Conf", "net-conf"
          "2026", "2026"
          "Amplifying FSharp", "amplifying-fsharp"
          "CSharp", "csharp"
          "Domain-driven design", "domain-driven-design"
          "FsAdvent", "fsadvent"
          "Functional programming", "functional-programming"
          "LINQ", "linq"
          "One-offs", "one-offs"
          "Open source", "open-source"
          "Series", "series"
          "Don't stop", "dont-stop"
          "Café & co", "cafe-and-co" ]

      for text, slug in values do
        Expect.equal (Slug.term text) slug $"'{text}' gives '{slug}'"
    }

    test "a term title shows F# and C#" {
      Expect.equal (Slug.termTitle "fsharp") "F#" "fsharp shows as F#"
      Expect.equal (Slug.termTitle "Amplifying FSharp") "Amplifying F#" "FSharp shows as F#"
      Expect.equal (Slug.termTitle "CSharp") "C#" "CSharp shows as C#"
      Expect.equal (Slug.termTitle "LINQ") "LINQ" "Another title stays"
    }
  ]
