module TwoPoint.Site.Render.Tests.CheckTests

open TwoPoint.Site.Render
open TwoPoint.Site.Render.Tests.Fixture

open Expecto

let private publishedWith fields slug body =
  Check.published Stage.Production [] slug (text fields body) |> Result.mapError _.Problems |> ok

let private publishedPost slug body = publishedWith frontMatter slug body

let private check (published: Post list) slug body =
  Check.post Stage.Production anyImages [] published slug (text frontMatter body)

let private target = publishedPost "a-post" "## First step\n\nText."

let private previewTime = System.DateTimeOffset(2026, 3, 5, 9, 30, 0, System.TimeSpan.Zero)

let private draftFrontMatter = "author: Matthew Watt\ntitle: A draft\nexcerpt: An excerpt"

let private draft (published: Post list) slug body =
  Check.draft Stage.Production anyImages [] previewTime published slug (text draftFrontMatter body)

let private categorizedDraft (published: Post list) slug body =
  Check.draft Stage.Production anyImages [] previewTime published slug (text $"{draftFrontMatter}\ncategory: One-offs" body)

[<Tests>]
let tests =
  testList "Check" [
    test "a heading change in a post that breaks an anchor link in another post is an error on the post" {
      let other = publishedPost "b-post" "Text.\n\nRead [the step](/posts/a-post/#first-step)."

      Expect.equal
        (check [ target; other ] "a-post" "## Second step\n\nText.").Problems
        [ Problem.error
            (Place.Post 1)
            "The change breaks a link in the post 'b-post', on body line 4: The page /posts/a-post/ has no heading with the id 'first-step'." ]
        "The error names the link in the other post"
    }

    test "a broken link in another post that the post does not cause gives no problem on the post" {
      let other = publishedPost "b-post" "Read [c](/posts/c-post/)."
      Expect.isEmpty (check [ target; other ] "a-post" "## First step\n\nNew text.").Problems "The post has no problem"
    }

    test "a broken link in the post is an error at its body line" {
      Expect.equal
        (check [ target ] "a-post" "Text.\n\nRead [c](/posts/c-post/).").Problems
        [ Problem.error (Place.Body 4) "The output has no page at /posts/c-post/." ]
        "The error is at the line of the link"
    }

    test "a new post can link to a published post and to a list page" {
      Expect.isEmpty
        (check [ target ] "b-post" "Read [the step](/posts/a-post/#first-step) and [the category](/category/one-offs/).").Problems
        "The links resolve"
    }

    test "a tag change that removes the last post of a tag page is an error on the post" {
      let tagged = publishedWith $"{frontMatter}\ntags:\n  - Fable" "a-post" "Text."
      let other = publishedPost "b-post" "See [the tag](/tag/fable/)."

      Expect.equal
        (check [ tagged; other ] "a-post" "Text.").Problems
        [ Problem.error (Place.Post 1) "The change breaks a link in the post 'b-post', on body line 2: The output has no page at /tag/fable/." ]
        "The tag page goes away with the tag"
    }

    test "a change of the message of a broken link in another post gives no problem on the post" {
      let tagged = publishedWith $"{frontMatter}\ntags:\n  - Fable" "a-post" "Text."
      let other = publishedPost "b-post" "See [the tag](/tag/fable)."
      Expect.isEmpty (check [ tagged; other ] "a-post" "Text.").Problems "The post does not cause the broken link"
    }

    test "a link to a file under /_/ gives no problem" {
      Expect.isEmpty (check [ target ] "a-post" "See [the script](/_/widgets/search.js).").Problems "Only the build knows the files under /_/"
    }

    test "only the parse of the post calls the image size port" {
      let fetch, calls = recording anyImage
      let other = publishedPost "b-post" "![A cat](https://twopointwebsite.blob.core.windows.net/assets/cat.jpg)"
      let dog = "https://twopointwebsite.blob.core.windows.net/assets/dog.jpg"

      Check.post Stage.Production (Images.ofFetch fetch) [] [ target; other ] "a-post" (text frontMatter $"![A dog]({dog})")
      |> ignore

      Expect.equal (List.ofSeq calls) [ dog ] "The check gets only the image of the post"
    }

    test "a tag with another title in a published post gives a warning that names the post" {
      let other = publishedWith $"{frontMatter}\ntags:\n  - Fable" "b-post" "Text."

      Expect.equal
        (Check.post Stage.Production anyImages [] [ other ] "a-post" (text $"{frontMatter}\ntags:\n  - 2026\n  - fable" "Text.")).Problems
        [ Problem.warning
            (Place.Field("tags", 9))
            "The tag 'fable' has the slug 'fable', and other posts give that slug another title: b-post ('Fable')." ]
        "The warning names the other post"
    }

    test "a post with an error has no article" {
      let checkedPost = check [ target ] "a-post" "```nolang\nx\n```"
      Expect.isNone checkedPost.Article "No article"
      Expect.isTrue (List.exists Problem.isError checkedPost.Problems) "The error is in the problems"
    }

    test "the article has the meta block, the title, and the body, and the outline apart" {
      let article = (check [ target ] "a-post" "## First step\n\nText.").Article.Value
      Expect.stringStarts article.Content """<dl class="front-matter">""" "The meta block is first"
      Expect.stringContains article.Content """<h1>A post</h1>""" "The title"
      Expect.stringContains article.Content """<h2 id="first-step">First step</h2>""" "The body"
      Expect.isFalse (article.Content.Contains "outline") "The content has no outline"
      Expect.stringStarts article.Outline """<aside class="outline" aria-label="Outline">""" "The outline"
      Expect.stringEnds article.Outline """<a href="#first-step">First step</a></li></ul></aside>""" "The link"
    }

    test "a draft with a title, an excerpt, and an author, and with no publish date and no category, has an article and the error of the category" {
      let checkedPost = draft [ target ] "b-post" "## A step\n\nText."
      Expect.equal checkedPost.Problems [ Problem.error (Place.Field("category", 1)) "The post has no category." ] "Only the category"
      Expect.isSome checkedPost.Article "The article"
    }

    test "a draft with a category and no publish date has no problem" {
      Expect.isEmpty (categorizedDraft [ target ] "b-post" "Text.").Problems "A draft can have no publish date"
    }

    test "the article of a draft shows the time of the preview as the publish date and shows no category" {
      let article = (draft [ target ] "b-post" "Text.").Article.Value.Content
      Expect.stringContains article """<time datetime="2026-03-05T09:30:00Z">2026-03-05</time>""" "The time of the preview"
      Expect.isFalse (article.Contains "One-offs") "No category"
      Expect.stringContains article "Matthew Watt" "The author"
    }

    test "a draft with a publish date keeps it, and a draft with a category is not changed" {
      let withBoth = $"{draftFrontMatter}\npublishDate: 2025-01-02T03:04:05Z\ncategory: One-offs"
      let article = (Check.draft Stage.Production anyImages [] previewTime [ target ] "b-post" (text withBoth "Text.")).Article.Value.Content
      Expect.stringContains article """<time datetime="2025-01-02T03:04:05Z">""" "The publish date of the draft"
    }

    test "a draft with no title, no excerpt, or no author is an error and has no article" {
      for field in [ "title"; "excerpt"; "author" ] do
        let lines = draftFrontMatter.Split '\n' |> Array.filter (fun line -> not (line.StartsWith $"{field}:"))
        let checkedPost = Check.draft Stage.Production anyImages [] previewTime [ target ] "b-post" (text (String.concat "\n" lines) "Text.")
        Expect.isNone checkedPost.Article $"No article without {field}"

        Expect.isTrue
          (checkedPost.Problems |> List.exists (fun problem -> Problem.isError problem && problem.Place = Place.Field(field, 1)))
          $"The error names {field}"
    }

    test "a draft in a series with a category other than Series is an error" {
      let record = seriesRecord "tour"
      let checkedPost = Check.draft Stage.Production anyImages [ record ] previewTime [] "b-post" (text $"{draftFrontMatter}\nseries: tour" "Text.")

      Expect.equal
        (checkedPost.Problems |> List.map _.Message)
        [ "The post is in a series, so give it the category 'Series'." ]
        "A series needs the category Series, also in a draft"
    }

    test "a draft with tags and no publish date gets no year warning" {
      let checkedPost = Check.draft Stage.Production anyImages [] previewTime [ target ] "b-post" (text $"{draftFrontMatter}\ncategory: One-offs\ntags:\n  - Fable" "Text.")
      Expect.isEmpty checkedPost.Problems "The year rule is for a post with a publish date"
    }

    test "a broken link in a draft is an error at its body line" {
      Expect.equal
        (categorizedDraft [ target ] "b-post" "Text.\n\nRead [c](/posts/c-post/).").Problems
        [ Problem.error (Place.Body 4) "The output has no page at /posts/c-post/." ]
        "The error is at the line of the link"
    }

    test "a draft can link to a published post" {
      Expect.isEmpty (categorizedDraft [ target ] "b-post" "See [the step](/posts/a-post/).").Problems "The link resolves"
    }

    test "forPublish gives the error of the build for a card image that the build cannot fetch" {
      let image = "https://example.com/card.jpg"
      let images = Images.ofFetch (fetchWith (fun _ -> Error "HTTP 404"))

      Expect.equal
        (Check.forPublish Stage.Production images [] [ target ] "b-post" (text $"{frontMatter}\nimage: {image}" "Text.")).Problems
        [ Problem.error
            (Place.Post 1)
            $"The front matter gives the Open Graph image {image}, and the build cannot make its crop: HTTP 404." ]
        "The error is the one of the build"
    }

    test "forPublish gives no problem for a card image that has a size" {
      let fields = $"{frontMatter}\nimage: https://example.com/card.jpg"
      Expect.isEmpty (Check.forPublish Stage.Production anyImages [] [ target ] "b-post" (text fields "Text.")).Problems "A card image"
    }

    test "post does not size the card image" {
      let fetch, calls = recording anyImage
      let fields = $"{frontMatter}\nimage: https://example.com/card.jpg"
      Check.post Stage.Production (Images.ofFetch fetch) [] [ target ] "b-post" (text fields "Text.") |> ignore
      Expect.isEmpty calls "The save check downloads no card image"
    }

    test "withoutPost gives the link that breaks in another post when the post leaves the site" {
      let other = publishedPost "b-post" "Text.\n\nRead [the step](/posts/a-post/#first-step)."

      Expect.equal
        (Check.withoutPost Stage.Production [ target; other ] "a-post")
        [ Problem.error
            (Place.Post 1)
            "The change breaks a link in the post 'b-post', on body line 4: The output has no page at /posts/a-post/." ]
        "The error names the link in the other post"
    }

    test "withoutPost gives no problem when no other post links to the post" {
      let other = publishedPost "b-post" "Text."
      Expect.isEmpty (Check.withoutPost Stage.Production [ target; other ] "a-post") "No broken link"
      Expect.isEmpty (Check.withoutPost Stage.Production [ other ] "a-post") "A post that is not published"
    }
  ]
