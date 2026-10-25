module TwoPoint.Admin.Tests.PreviewTests

open TwoPoint.Admin

open Expecto

let private noDetails: Api.PostDetails =
  { Author = None
    Category = None
    Tags = []
    Series = None
    Image = None
    ShowOnHomepage = true }

let private post: Api.PostDocument =
  { Slug = "my-post"
    State = Api.Draft
    PendingEdit = false
    PublishDate = None
    ETag = Some "\"v1\""
    Title = "My post"
    Excerpt = "About it."
    Body = "Some text.\n"
    Details = noDetails
    Text = ""
    PublishedText = None }

let private opened =
  let model, _ = Shell.init "/posts/my-post"
  let model, _ = Shell.update (Shell.Session(Session.Started true)) model
  fst (Shell.update (Shell.Editor(Editor.Answered("my-post", Ok post))) model)

let private send msg (model: Shell.Model) = Shell.update (Shell.Editor msg) model

let private preview (model: Shell.Model) =
  match model.Editor with
  | Some(Editor.Loaded document) -> document.Preview
  | other -> failtestf "Expected a loaded editor, got %A" other

let private article: Api.PostPreview =
  { Article = Some "<h1>My post</h1>"
    Problems = [] }

[<Tests>]
let tests =
  testList "Preview" [
    test "Preview sends the fields on the screen, also before a save" {
      let model, _ = send (Editor.BodyChanged "Unsaved text.\n") opened
      let model, effect = send Editor.PreviewToggled model

      Expect.equal
        effect
        (Some(
          Shell.Send
            [ Shell.PreviewPost
                { Slug = "my-post"
                  Fields = { Title = "My post"; Excerpt = "About it."; Body = "Unsaved text.\n" } } ]
        ))
        "The call has the unsaved text"

      Expect.equal (preview model) Preview.Loading "The preview waits for the API"
    }

    test "the answer of the API shows the article in the place of the text" {
      let model, _ = send Editor.PreviewToggled opened
      let model, effect = send (Editor.PreviewAnswered("my-post", Ok article)) model
      Expect.isNone effect "No more work"
      Expect.equal (preview model) (Preview.Shown article) "The article"
    }

    test "Esc goes back to the text and keeps it" {
      let model, _ = send (Editor.BodyChanged "Unsaved text.\n") opened
      let model, _ = send Editor.PreviewToggled model
      let model, _ = send (Editor.PreviewAnswered("my-post", Ok article)) model
      let model, effect = send Editor.PreviewClosed model
      Expect.isNone effect "No call"
      Expect.equal (preview model) Preview.Closed "The text shows"

      match model.Editor with
      | Some(Editor.Loaded document) -> Expect.equal document.Fields.Body "Unsaved text.\n" "The text stays"
      | other -> failtestf "Expected a loaded editor, got %A" other
    }

    test "the Preview button a second time goes back to the text" {
      let model, _ = send Editor.PreviewToggled opened
      let model, _ = send (Editor.PreviewAnswered("my-post", Ok article)) model
      let model, effect = send Editor.PreviewToggled model
      Expect.isNone effect "No call"
      Expect.equal (preview model) Preview.Closed "The text shows"
    }

    test "an answer that comes after Esc is ignored" {
      let model, _ = send Editor.PreviewToggled opened
      let model, _ = send Editor.PreviewClosed model
      let model, _ = send (Editor.PreviewAnswered("my-post", Ok article)) model
      Expect.equal (preview model) Preview.Closed "The text stays on the screen"
    }

    test "a failed preview shows the reason, and Esc goes back" {
      let model, _ = send Editor.PreviewToggled opened
      let model, _ = send (Editor.PreviewAnswered("my-post", Error(Api.Failed "The front matter of the stored post is not readable"))) model
      Expect.equal (preview model) (Preview.Failed "The front matter of the stored post is not readable") "The reason"
      let model, _ = send Editor.PreviewClosed model
      Expect.equal (preview model) Preview.Closed "The text shows"
    }

    test "a refused token on a preview offers another account and keeps the text" {
      let model, _ = send (Editor.BodyChanged "x") opened
      let model, _ = send Editor.PreviewToggled model
      let model, _ = send (Editor.PreviewAnswered("my-post", Error Api.Refused)) model
      Expect.equal model.Session.Status Session.Refused "The banner"

      match model.Editor with
      | Some(Editor.Loaded document) -> Expect.equal document.Fields.Body "x" "The text stays"
      | other -> failtestf "Expected a loaded editor, got %A" other
    }

    test "a preview of another post is ignored" {
      let model, _ = send Editor.PreviewToggled opened
      let model, _ = send (Editor.PreviewAnswered("other", Ok article)) model
      Expect.equal (preview model) Preview.Loading "The preview still waits"
    }

    test "the preview does not start a save or change the save state" {
      let model, _ = send (Editor.BodyChanged "x") opened
      let model, _ = send Editor.PreviewToggled model
      Expect.equal (Editor.statusText (match model.Editor with Some(Editor.Loaded document) -> document | _ -> failtest "no editor")) "Draft · unsaved changes" "The status"
    }

    test "the frame links the site CSS and puts the outline next to the article" {
      let html =
        Preview.frame "<dl class=\"front-matter\"></dl><h1>T</h1><div class=\"prose\"></div><aside class=\"outline\" aria-label=\"Outline\"><ul></ul></aside>"

      Expect.stringContains html "<link rel=\"stylesheet\" href=\"/preview-styles/site.css\">" "The CSS"

      Expect.stringContains
        html
        "<main class=\"post-page\"><article class=\"post\"><dl class=\"front-matter\"></dl><h1>T</h1><div class=\"prose\"></div></article><aside class=\"outline\" aria-label=\"Outline\"><ul></ul></aside></main>"
        "The article and the outline"
    }

    test "the frame of an article with no outline has the article only" {
      Expect.stringContains
        (Preview.frame "<h1>T</h1>")
        "<main class=\"post-page\"><article class=\"post\"><h1>T</h1></article></main>"
        "The article"
    }

    test "a problem shows its place and its message" {
      let problem place message : Api.PreviewProblem =
        { Severity = "error"; Place = place; Message = message }

      Expect.equal (Preview.problemText (problem "Body line 4" "The output has no page.")) "Body line 4: The output has no page." "A body line"
      Expect.equal (Preview.problemText (problem "Field category" "No category.")) "Field category: No category." "A field"
      Expect.equal (Preview.problemText (problem "Post" "The change breaks a link.")) "The change breaks a link." "The whole post"
    }
  ]
