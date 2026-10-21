module TwoPoint.Site.Generator.Tests.RenderMapTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto
open System
open System.IO
open System.Text

/// `files` gives each file as its path from the repo root and its text.
let private loadPostsFrom (files: (string * string) list) =
  let root = Path.Combine(Path.GetTempPath(), $"twopoint-content-{Guid.NewGuid()}")

  try
    for path, text in files do
      let full = Path.Combine(root, path)
      Directory.CreateDirectory(Path.GetDirectoryName full) |> ignore
      File.WriteAllText(full, text)

    Site.loadPosts anyImages root
  finally
    Directory.Delete(root, true)

let private seriesFile slug = $"frontend/content/series/{slug}.yml", "title: A series\nexcerpt: Posts\n"

[<Tests>]
let tests =
  testList "RenderMap" [
    test "the stage marker contains the stage name" {
      for stage in Stage.all do
        let marker = RenderMap.create stage buildTime widgets (content []) |> Map.find StageMarker
        Expect.equal (Encoding.UTF8.GetString(marker ())) (Stage.name stage) "The marker contains the stage name"
    }

    test "a series record gives the series page, the cards, and the links of its posts" {
      let fields = frontMatter.Replace("One-offs", "Series") + "\nseries: fable-reminders"
      let post = publishedIn [ fableReminders ] "part-1" (text fields "Text.")

      let map = RenderMap.create Stage.Production buildTime widgets (content [ post ])
      let seriesPage = siteText map "series/fable-reminders/index.html"
      Expect.stringContains seriesPage "<h1>Fable Reminders</h1>" "The series page has the title of the record"
      Expect.stringContains seriesPage """<p class="excerpt">Notes on Fable</p>""" "The series page has the excerpt of the record"
      Expect.stringContains (siteText map "series/index.html") "1 post &#183; Notes on Fable" "The series list has a card for the series"
      Expect.stringContains (siteText map "posts/part-1/index.html") "href=\"/series/fable-reminders/\"" "The post links to its series"
    }

    test "a series record with no published post gives no series page and no link" {
      let posts =
        loadPostsFrom
          [ seriesFile "a-series"
            seriesFile "no-posts"
            file "a-post", text $"{frontMatter}\nseries: a-series" "Text." ]
        |> ok

      let map = RenderMap.create Stage.Production buildTime widgets (content posts)

      Expect.contains (pagePaths map) "series/a-series/index.html" "The series with a post has a page"
      Expect.isFalse (pagePaths map |> Set.exists (fun path -> path.Contains "no-posts")) "The series with no post has no page"

      for KeyValue(output, render) in map do
        Expect.isFalse (Encoding.UTF8.GetString(render ()).Contains "/series/no-posts/") $"%A{output} has no link to the series"
    }

    test "a post with an unknown series slug fails with its file and line" {
      Expect.equal
        (loadPostsFrom [ seriesFile "a-series"; file "a-post", text $"{frontMatter}\nseries: b-series" "Text." ]
         |> Result.mapError (List.map PostError.format))
        (Error [ "frontend/content/posts/a-post.md:7: No series has the slug 'b-series'." ])
        "The build error names the file and the line of the series field"
    }
  ]
