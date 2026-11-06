module TwoPoint.Core.Tests.PostTermsTests

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Queries
open TwoPoint.Core.Util

open Expecto
open FsToolkit.ErrorHandling
open IcedTasks

open System.Threading

let private slug value = Slug.Unsafe.create value

let private stored name text : StoredPost = { Slug = slug name; Text = text; ETag = "v1" }

let private post slug (date : string) (category : string) (tags : string list) =
  let tagLines = tags |> List.map (sprintf "  - %s\n") |> String.concat ""
  stored slug $"---\ntitle: {slug}\npublishDate: {date}T00:00:00Z\ncategory: {category}\ntags:\n{tagLines}---\n\nBody."

type private FakeStore(published : StoredPost list, series : StoredSeries list) =
  interface GetTerms.Dependencies with
    member _.GetPublishedPosts () = CancellableTaskResult.singleton published

  interface GetSeries.Dependencies with
    member _.GetUtcNow () = System.DateTime.UtcNow
    member _.GetSeriesRecords () = CancellableTaskResult.singleton series
    member _.GetPublishedPosts () = CancellableTaskResult.singleton published
    member _.GetWorkingPosts () = CancellableTaskResult.singleton []

let private terms published =
  match (GetTerms.withDependencies (FakeStore(published, [])) () CancellationToken.None).Result with
  | Ok suggestions -> suggestions
  | Error error -> failtestf "Expected terms, got %A" error

let private seriesList records =
  match (GetSeries.withDependencies (FakeStore([], records)) () CancellationToken.None).Result with
  | Ok listing -> listing
  | Error error -> failtestf "Expected a list, got %A" error

[<Tests>]
let tests =
  testList "Terms and series" [
    test "a term has the number of published posts that use it, and the most used term comes first" {
      let suggestions =
        terms
          [ post "a" "2026-01-01" "Dev" [ "FSharp"; "Testing" ]
            post "b" "2026-01-02" "Dev" [ "FSharp" ]
            post "c" "2026-01-03" "Life" [] ]

      Expect.equal
        suggestions.Categories
        [ { Slug = slug "dev"; Title = "Dev"; Value = "Dev"; Posts = 2 }
          { Slug = slug "life"; Title = "Life"; Value = "Life"; Posts = 1 } ]
        "Categories"

      Expect.equal
        suggestions.Tags
        [ { Slug = slug "fsharp"; Title = "F#"; Value = "FSharp"; Posts = 2 }
          { Slug = slug "testing"; Title = "Testing"; Value = "Testing"; Posts = 1 } ]
        "Tags"
    }

    test "two titles with one slug count as one term, and the newest post gives the title" {
      let suggestions =
        terms
          [ post "old" "2026-01-01" "Dev" [ "Functional programming" ]
            post "new" "2026-02-01" "Dev" [ "functional Programming" ] ]

      let expected =
        { Slug = slug "functional-programming"
          Title = "functional Programming"
          Value = "functional Programming"
          Posts = 2 }

      Expect.equal suggestions.Tags [ expected ] "One term"
    }

    test "a post that gives one tag two times counts once" {
      let suggestions = terms [ post "a" "2026-01-01" "Dev" [ "Testing"; "testing" ] ]
      Expect.equal (suggestions.Tags |> List.map _.Posts) [ 1 ] "One post"
    }

    test "the category Series is not a suggestion" {
      let suggestions = terms [ post "a" "2026-01-01" "Series" [] ]
      Expect.isEmpty suggestions.Categories "No categories"
    }

    test "a post with no category and no tags adds no term" {
      let suggestions = terms [ stored "a" "---\ntitle: A\npublishDate: 2026-01-01T00:00:00Z\n---\n" ]
      Expect.isEmpty suggestions.Categories "No categories"
      Expect.isEmpty suggestions.Tags "No tags"
    }

    test "the series list has the slug and the title of each readable record, in title order" {
      let listing =
        seriesList
          [ { Slug = "zeta"; Text = "title: Zeta\nexcerpt: Last.\n"; ETag = "v1" }
            { Slug = "alpha"; Text = "title: Alpha\nexcerpt: First.\n"; ETag = "v1" }
            { Slug = "broken"; Text = "title: [\n"; ETag = "v1" } ]

      Expect.equal (listing |> List.map (fun series -> series.Slug, series.Title)) [ slug "alpha", "Alpha"; slug "zeta", "Zeta" ] "The list"
    }
  ]
