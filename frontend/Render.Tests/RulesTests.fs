module TwoPoint.Site.Render.Tests.RulesTests

open TwoPoint.Site.Render
open TwoPoint.Site.Render.Tests.Fixture

open Expecto
open System

let private problems result =
  match result with
  | Ok(_, problems: FileProblems)
  | Error problems -> problems.Problems

/// The field starts on file line 7, after the fixture fields.
let private withField field body = text $"{frontMatter}\n{field}" body

let private withTags (tags: string list) =
  withField ("tags:" + (tags |> List.map (fun tag -> $"\n  - '{tag}'") |> String.concat "")) "Text."

let private assets = "https://twopointwebsite.blob.core.windows.net/assets/a4dd66d174d2f471.jpg"

let private draftFields: RuleFields =
  { PublishDate = None
    Category = None
    CategoryLine = 1
    Tags = []
    Series = None
    TagsLine = 1 }

[<Tests>]
let tests =
  testList "Post rules" [
    test "a post with no category is an error on the category field" {
      Expect.equal
        (problems (parse "a-post" (text (frontMatter.Replace("\ncategory: One-offs", "")) "Text.")))
        [ Problem.error (Place.Field("category", 1)) "The post has no category." ]
        "A missing field is on the opening line"
    }

    test "a draft with no category gives no error at a save" {
      Expect.isEmpty (Rules.frontMatter PostState.Draft draftFields) "A draft can have no category"

      Expect.equal
        (Rules.frontMatter PostState.Published draftFields)
        [ Problem.error (Place.Field("category", 1)) "The post has no category." ]
        "The same fields are an error on a published post"
    }

    test "a post in a series with another category is an error on the category field" {
      Expect.equal
        (problems (parseIn [ seriesRecord "a-series" ] "a-post" (withField "series: a-series" "Text.")))
        [ Problem.error (Place.Field("category", 6)) "The post is in a series, so give it the category 'Series'." ]
        "One-offs is not Series"

      Expect.equal
        (Rules.frontMatter PostState.Draft { draftFields with Series = Some "a-series" })
        [ Problem.error (Place.Field("category", 1)) "The post is in a series, so give it the category 'Series'." ]
        "A draft in a series with no category"

      Expect.isEmpty
        (problems (parseIn [ seriesRecord "a-series" ] "a-post" (text (seriesFrontMatter "a-series") "Text.")))
        "Series is correct"
    }

    test "the category Series with no series is an error on the category field" {
      Expect.equal
        (problems (parse "a-post" (text (frontMatter.Replace("One-offs", "Series")) "Text.")))
        [ Problem.error
            (Place.Field("category", 6))
            "The category is 'Series', so give the post a series, or give it another category." ]
        "Series needs a series"
    }

    test "two tags with one slug are an error on the second tag" {
      Expect.equal
        (problems (parse "a-post" (withTags [ "FSharp"; "2026"; "fsharp" ])))
        [ Problem.error (Place.Field("tags", 10)) "Another tag has the slug 'fsharp'. Delete one of the two tags." ]
        "fsharp is on file line 10"
    }

    test "a post with no tags has no problem" {
      Expect.isEmpty (problems (parse "a-post" (text frontMatter "Text."))) "No tags"
    }

    test "a post with no tag for its publish year gets a warning on the tags field" {
      Expect.equal
        (problems (parse "a-post" (withTags [ "FSharp" ])))
        [ Problem.warning (Place.Field("tags", 7)) "Add the tag '2026' for the publish year." ]
        "The tags field is on file line 7"
    }

    test "a year tag for another year gets a warning on the tag" {
      Expect.equal
        (problems (parse "a-post" (withTags [ "2026"; "2025" ])))
        [ Problem.warning
            (Place.Field("tags", 9))
            "The tag '2025' is a year tag, and the publish year is 2026. Delete the tag or change it to '2026'." ]
        "2025 is on file line 9"

      Expect.isEmpty (problems (parse "a-post" (withTags [ "FSharp"; "2026" ]))) "The tag of the publish year"
    }

    test "a draft with no publish date gets no year tag warning" {
      Expect.isEmpty
        (Rules.frontMatter
          PostState.Draft
          { draftFields with
              Category = Some { Slug = "one-offs"; Title = "One-offs" }
              CategoryLine = 2
              Tags = [ { Term = { Slug = "2025"; Title = "2025" }; Line = 4 } ] })
        "Only a post with a publish date"
    }

    test "a term that another published post gives another title gets a warning that names the post" {
      let post = postWith "a-post" "2026-01-01T00:00:00Z" "One-offs" [ "Functional programming"; "2026" ] None
      let older = postWith "b-post" "2025-01-01T00:00:00Z" "one-offs" [ "functional programming"; "2025" ] None
      let same = postWith "c-post" "2025-06-01T00:00:00Z" "One-offs" [ "Functional programming"; "2025" ] None

      Expect.equal
        (Rules.termTitles [ post; older; same ] post)
        [ Problem.warning
            (Place.Field("category", 6))
            "The category 'One-offs' has the slug 'one-offs', and other posts give that slug another title: b-post ('one-offs')."
          Problem.warning
            (Place.Field("tags", 8))
            "The tag 'Functional programming' has the slug 'functional-programming', and other posts give that slug another title: b-post ('functional programming')." ]
        "A category and a tag"

      Expect.isEmpty (Rules.termTitles [ post; same ] post) "The same titles give no problem"
    }

    test "the current text of a post replaces its published version for the term titles" {
      let published = postWith "a-post" "2026-01-01T00:00:00Z" "One-offs" [ "functional programming" ] None
      let current = postWith "a-post" "2026-01-01T00:00:00Z" "One-offs" [ "Functional programming" ] None
      Expect.isEmpty (Rules.termTitles [ published ] current) "A post does not disagree with itself"
    }

    test "an image with an empty alt text gets a warning on its body line" {
      for alt in [ ""; " " ] do
        Expect.equal
          (problems (parse "a-post" (text frontMatter $"Text.\n\n![{alt}]({assets})")))
          [ Problem.warning
              (Place.Body 4)
              "The image has no alt text. Write the alt text between the brackets, for example ![A cat](…)." ]
          $"The alt text '{alt}'"
    }

    test "an image outside the asset container gets a warning on its body line" {
      let outside = "https://example.com/cat.jpg"

      Expect.equal
        (problems (parse "a-post" (text frontMatter $"Text.\n\n![A cat]({outside})")))
        [ Problem.warning
            (Place.Body 4)
            $"The image {outside} is not in the asset container. Upload the image, so that the site controls it." ]
        "example.com is not the asset container"

      Expect.isEmpty (problems (parse "a-post" (text frontMatter $"![A cat]({assets})"))) "An asset"
    }

    test "on local, an image in the Azurite asset container is an asset" {
      let azurite = Uri "http://127.0.0.1:10000/devstoreaccount1/assets/a4dd66d174d2f471.jpg"
      Expect.isTrue (AssetContainer.isUrl Stage.Local azurite) "The local backend writes the asset to Azurite"
      Expect.isFalse (AssetContainer.isUrl Stage.Local (Uri "http://127.0.0.1:10000/devstoreaccount1/posts/a.jpg")) "Another container"
      Expect.isFalse (AssetContainer.isUrl Stage.Staging azurite) "Only on local"
    }

    test "a body warning does not stop the post, and a body error keeps the front matter warnings" {
      match parse "a-post" (withField "tags:\n  - FSharp" $"![]({assets})") with
      | Ok(_, problems) ->
        Expect.equal (List.map _.Severity problems.Problems) [ Severity.Warning; Severity.Warning ] "Two warnings"
      | Error problems -> failtest $"Warnings only: %A{problems}"

      Expect.equal
        (problems (parse "a-post" (withField "tags:\n  - FSharp" "Text.\n\n```js\nlet a = 1\n```")))
        [ Problem.warning (Place.Field("tags", 7)) "Add the tag '2026' for the publish year."
          Problem.error
            (Place.Body 4)
            "The code block has the unknown language 'js'. Use one of these languages: fsharp, csharp, swift, sh, diff, json, xml, css." ]
        "The error and the warning"
    }
  ]
