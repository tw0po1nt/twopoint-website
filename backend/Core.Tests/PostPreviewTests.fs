module TwoPoint.Core.Tests.PostPreviewTests

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Util
open TwoPoint.Site.Render

open Expecto
open FsToolkit.ErrorHandling
open IcedTasks

open System.Threading

let private published =
  "---\npublishDate: 2026-02-04T13:00:00Z\nauthor: Matthew Watt\ntitle: Live\nexcerpt: An excerpt\ncategory: One-offs\n---\n\nLive body.\n"

let private inSeries =
  "---\npublishDate: 2026-02-04T13:00:00Z\nauthor: Matthew Watt\ntitle: Part\nexcerpt: An excerpt\ncategory: Series\nseries: tour\n---\n\nBody.\n"

let private images =
  { new IImageSize with
      member _.Size _ = Result.Ok { Width = 1600; Height = 900 } }

type private FakeStore(published : (string * string) list, working : (string * string) list, series : (string * string) list) =

  interface PreviewPost.Dependencies with
    member _.Stage = Stage.Staging
    member _.Images = images
    member _.GetUtcNow () = System.DateTime(2026, 3, 5, 9, 30, 0, System.DateTimeKind.Utc)

    member _.GetWorkingBlob slug =
      working
      |> List.tryFind (fun (name, _) -> name = Slug.value slug)
      |> Option.map (fun (_, text) -> { Text = text; ETag = "v1" })
      |> CancellableTaskResult.singleton

    member _.GetPublishedPosts () =
      published
      |> List.map (fun (name, text) -> { StoredPost.Slug = Slug.Unsafe.create name; Text = text })
      |> CancellableTaskResult.singleton

    member _.GetSeriesRecords () =
      series |> List.map (fun (slug, text) -> { StoredSeries.Slug = slug; Text = text }) |> CancellableTaskResult.singleton

let private previewWith (store : FakeStore) slug details title body =
  let task =
    PreviewPost.withDependencies
      store
      { Slug = slug; Title = title; Excerpt = "New excerpt"; Body = body; Details = details }

  (task CancellationToken.None).Result

let private preview (store : FakeStore) slug title body = previewWith store slug None title body

let private contentOf (checkedPost : Check.CheckedPost) =
  checkedPost.Article |> Option.map _.Content |> Option.defaultValue ""

let private articleOf result =
  match result with
  | Ok(checkedPost : Check.CheckedPost) -> checkedPost
  | Error error -> failtestf "Expected a preview, got %A" error

[<Tests>]
let tests =
  testList "Preview a post" [
    test "the preview of unsaved text gives its HTML and no problem" {
      let store = FakeStore([ "live", published ], [], [])
      let result = preview store "live" "Edited title" "Unsaved text.\n\n## A step\n" |> articleOf

      Expect.isEmpty result.Problems "No problem"
      let html = contentOf result
      Expect.stringContains html "<h1>Edited title</h1>" "The title of the editor"
      Expect.stringContains html "Unsaved text." "The body of the editor"
      Expect.stringContains result.Article.Value.Outline "href=\"#a-step\"" "The outline"
      Expect.stringContains html "class=\"front-matter\"" "The meta block"
    }

    test "the preview keeps the front matter of the working blob" {
      let working = published.Replace("author: Matthew Watt", "author: A guest")
      let store = FakeStore([ "live", published ], [ "live", working ], [])
      let html = contentOf (preview store "live" "T" "Body.\n" |> articleOf)
      Expect.stringContains html "A guest" "The author of the pending edit"
    }

    test "the preview of text with a broken link gives the problem at its body line" {
      let store = FakeStore([ "live", published ], [], [])
      let result = preview store "live" "T" "Text.\n\nRead [c](/posts/missing/).\n" |> articleOf

      Expect.equal
        result.Problems
        [ Problem.error (Place.Body 4) "The output has no page at /posts/missing/." ]
        "The problem"
    }

    test "the preview of a draft with a title, an excerpt, and an author, and with no publish date and no category, gives an article" {
      let draft = "---\ntitle: Old\nexcerpt: An excerpt\nauthor: Matthew Watt\n---\n"
      let store = FakeStore([ "live", published ], [ "draft", draft ], [])
      let result = preview store "draft" "New title" "Body.\n" |> articleOf

      Expect.isEmpty result.Problems "A draft can have no publish date and no category"
      let html = contentOf result
      Expect.stringContains html "<h1>New title</h1>" "The title"
      Expect.stringContains html "<time datetime=\"2026-03-05T09:30:00Z\">" "The time of the preview is the publish date"
    }

    test "the preview of a draft with no author gives the error and no article" {
      let store = FakeStore([], [ "draft", "---\ntitle: Old\n---\n" ], [])
      let result = preview store "draft" "T" "Body.\n" |> articleOf

      Expect.isNone result.Article "No article"
      Expect.isTrue (result.Problems |> List.exists (fun problem -> problem.Place = Place.Field("author", 1))) "No author"
      Expect.isFalse (result.Problems |> List.exists (fun problem -> problem.Place = Place.Field("publishDate", 1))) "No problem about the publish date"
    }

    test "the preview shows the series and the author of the request details before a save" {
      let draft = "---\ntitle: Old\nexcerpt: An excerpt\nauthor: Matthew Watt\n---\n"
      let store = FakeStore([], [ "draft", draft ], [ "tour", "title: Tour\nexcerpt: A tour\n" ])

      let details : TwoPoint.Site.Render.PostText.Details =
        { Author = Some "A guest"
          Category = Some "Series"
          Tags = []
          Series = Some "tour"
          Image = None
          ShowOnHomepage = true }

      let withDetails = previewWith store "draft" (Some details) "T" "Body.\n" |> articleOf
      Expect.isEmpty withDetails.Problems "The series and the category agree"
      Expect.stringContains (contentOf withDetails) "href=\"/series/tour/\"" "The series of the request"
      Expect.stringContains (contentOf withDetails) "A guest" "The author of the request"

      let without = preview store "draft" "T" "Body.\n" |> articleOf
      Expect.isFalse ((contentOf without).Contains "/series/tour/") "The stored text has no series"
    }

    test "the preview reports a series in the request details whose category is not Series" {
      let draft = "---\ntitle: Old\nexcerpt: An excerpt\nauthor: Matthew Watt\n---\n"
      let store = FakeStore([], [ "draft", draft ], [ "tour", "title: Tour\nexcerpt: A tour\n" ])

      let details : TwoPoint.Site.Render.PostText.Details =
        { Author = Some "Matthew Watt"
          Category = Some "One-offs"
          Tags = []
          Series = Some "tour"
          Image = None
          ShowOnHomepage = true }

      let result = previewWith store "draft" (Some details) "T" "Body.\n" |> articleOf
      Expect.isNone result.Article "No article"
      Expect.isTrue
        (result.Problems |> List.exists (fun problem -> problem.Message.Contains "give it the category 'Series'"))
        "The category problem"
    }

    test "the preview finds the series record of the post" {
      let record = "title: Tour\nexcerpt: A tour\n"
      let withRecord = FakeStore([ "part", inSeries ], [], [ "tour", record ])
      let withoutRecord = FakeStore([ "part", inSeries ], [], [])

      Expect.isEmpty (preview withRecord "part" "T" "Body.\n" |> articleOf).Problems "The series exists"

      let missing = preview withoutRecord "part" "T" "Body.\n" |> articleOf
      Expect.isNone missing.Article "A post of an unknown series has no article"
      Expect.stringContains (missing.Problems |> List.head).Message "No series has the slug 'tour'" "The reason"
    }

    test "the preview of a post that does not exist fails" {
      match preview (FakeStore([], [], [])) "missing" "T" "B" with
      | Error(ActionError.Logic(PreviewPost.Error.PostNotFound missing)) -> Expect.equal (Slug.value missing) "missing" "The slug"
      | other -> failtestf "Expected PostNotFound, got %A" other
    }

    test "the preview of a post whose front matter is not readable fails with the reason" {
      let store = FakeStore([], [ "draft", "---\ntitle: [\n---\n" ], [])

      match preview store "draft" "T" "B" with
      | Error(ActionError.Logic(PreviewPost.Error.FrontMatterUnreadable reason)) -> Expect.isNotEmpty reason "The reason"
      | other -> failtestf "Expected FrontMatterUnreadable, got %A" other
    }

    test "a published post that does not parse does not stop the preview of another post" {
      let store = FakeStore([ "live", published; "broken", "no front matter" ], [], [])
      Expect.isEmpty (preview store "live" "T" "Body.\n" |> articleOf).Problems "No problem"
    }
  ]
