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

    test "the link check of the other posts calls no image size port" {
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

    test "the article has the meta block, the title, the body, and the outline" {
      let article = (check [ target ] "a-post" "## First step\n\nText.").Article |> Option.defaultValue ""
      Expect.stringStarts article """<dl class="front-matter">""" "The meta block is first"
      Expect.stringContains article """<h1>A post</h1>""" "The title"
      Expect.stringContains article """<h2 id="first-step">First step</h2>""" "The body"
      Expect.stringEnds article """<a href="#first-step">First step</a></li></ul></aside>""" "The outline is last"
    }
  ]
