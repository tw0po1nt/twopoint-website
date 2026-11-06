module TwoPoint.Site.Generator.Tests.RenderMapTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture
open TwoPoint.Site.Render
open TwoPoint.Site.Render.Tests.Fixture

open Expecto
open System.IO
open System.Text

/// `files` gives each file as its path from the repo root and its text.
let private loadPostsFrom (files: (string * string) list) =
  let postFiles container =
    files
    |> List.filter (fun (path, _) -> path.EndsWith(Container.extension container))
    |> List.map (fun (path, text) -> { Path = path; Bytes = Encoding.UTF8.GetBytes text })

  Site.parsePosts anyImages Stage.Production { Published = postFiles Container.Published; Series = postFiles Container.Series }

let private seriesFile slug =
  PostFolder.filePath PostFolder.pulled Container.Series $"{slug}.yml", "title: A series\nexcerpt: Posts\n"

type private FixtureStore() =
  interface IPostStore with
    member _.Download container =
      let files =
        match container with
        | Container.Published -> realPostFiles.Value.Published
        | Container.Series -> realPostFiles.Value.Series

      Ok(files |> List.rev |> List.map (fun file -> Path.GetFileName file.Path, file.Bytes))

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
            file "a-post", text (seriesFrontMatter "a-series") "Text." ]
        |> ok
        |> fst

      let map = RenderMap.create Stage.Production buildTime widgets (content posts)

      Expect.contains (pagePaths map) "series/a-series/index.html" "The series with a post has a page"
      Expect.isFalse (pagePaths map |> Set.exists (fun path -> path.Contains "no-posts")) "The series with no post has no page"

      for KeyValue(output, render) in map do
        Expect.isFalse (Encoding.UTF8.GetString(render ()).Contains "/series/no-posts/") $"%A{output} has no link to the series"
    }

    test "two titles for one tag slug give the title of the newer post on each page" {
      let older = published "older-post" (text $"{frontMatter}\ntags:\n  - Web development" "Text.")
      let newerFields = frontMatter.Replace("2026-02-04", "2026-03-04") + "\ntags:\n  - web development"
      let newer = published "newer-post" (text newerFields "Text.")

      let map = RenderMap.create Stage.Production buildTime widgets (content [ older; newer ])

      for slug in [ "older-post"; "newer-post" ] do
        Expect.stringContains (siteText map $"posts/{slug}/index.html") """<a href="/tag/web-development/">#web development</a>""" $"The page of {slug} has the newer title"

      Expect.stringContains (siteText map "tag/web-development/index.html") "Posts by tag &#39;web development&#39;" "The tag page has the newer title"

      for path in pagePaths map do
        Expect.isFalse ((siteText map path).Contains "#Web development") $"{path} has no older title"
    }

    test "a post with an unknown series slug fails with its file and line" {
      Expect.equal
        (loadPostsFrom [ seriesFile "a-series"; file "a-post", text $"{frontMatter}\nseries: b-series" "Text." ]
         |> Result.mapError FileProblems.formatAll)
        (Error [ "frontend/content/posts/published/a-post.md:7: No series has the slug 'b-series'." ])
        "The build error names the file and the line of the series field"
    }
  
    test "a build with only warnings passes, and a failed build prints each warning" {
      let warning = FileProblems.create (file "a-post") 8 [ Problem.warning (Place.Body 2) "A warning." ]
      let post = published "a-post" (text frontMatter "Text.")
      let broken = published "b-post" (text frontMatter "Read [c](/posts/c-post/).")

      Expect.equal
        (Site.renderMap Stage.Production buildTime widgets { content [ post ] with Warnings = [ warning ] }
         |> Result.map snd)
        (Ok [ warning ])
        "A warning does not fail the build, and the build keeps it"

      Expect.equal
        (Site.renderMap Stage.Production buildTime widgets { content [ post; broken ] with Warnings = [ warning ] }
         |> Result.map ignore)
        (Error
          [ errorAt "a-post" 9 "warning: A warning."
            errorAt "b-post" 9 "The output has no page at /posts/c-post/." ])
        "The failed build prints the warning with the error"
    }

    test "pull gives the posts that build makes into the same site" {
      let pulled = Pull.run (FixtureStore()) |> Result.mapError List.singleton |> ok
      let fromPull = Site.loadContent anyImages Stage.Production realRoot.Value pulled |> ok

      let render content =
        RenderMap.create Stage.Production buildTime widgets content
        |> Map.map (fun _ render -> render ())

      Expect.equal (render fromPull) (render realContent.Value) "Each output file has the same bytes"
    }
  
    test "the site paths of the render map agree with the library" {
      let map = RenderMap.create Stage.Production buildTime widgets realContent.Value
      Expect.equal (pagePaths map) (SitePath.all realPosts.Value) "The pages and the fixed files are the same"
    }
  ]
