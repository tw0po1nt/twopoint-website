module TwoPoint.Admin.Tests.DetailsTests

open TwoPoint.Admin

open Expecto

let private noDetails: Api.PostDetails =
  { Author = None
    Category = None
    Tags = []
    Series = None
    Image = None
    ShowOnHomepage = true }

let private document state eTag details : Api.PostDocument =
  { Slug = "my-post"
    State = state
    PendingEdit = false
    PublishDate = None
    ETag = eTag
    Title = "My post"
    Excerpt = "About it."
    Body = "Some text.\n"
    Details = details
    Text = ""
    PublishedText = None }

let private savedAs eTag : Api.SavedPost =
  { ETag = eTag
    Problems = []
    CheckFailed = false }

let private choices: Details.Choices =
  { Terms =
      { Categories = [ { Slug = "dev"; Title = "Dev"; Value = "Dev"; Posts = 4 }; { Slug = "life"; Title = "Life"; Value = "Life"; Posts = 1 } ]
        Tags =
          [ { Slug = "fsharp"; Title = "F#"; Value = "FSharp"; Posts = 6 }
            { Slug = "functional-programming"; Title = "Functional programming"; Value = "Functional programming"; Posts = 3 }
            { Slug = "testing"; Title = "Testing"; Value = "Testing"; Posts = 2 } ] }
    Series = [ { Slug = "my-series"; Title = "My series" } ] }

let private started =
  let model, _ = Shell.init "/posts/my-post"
  fst (Shell.update (Shell.Session(Session.Started true)) model)

let private opened (post: Api.PostDocument) =
  fst (Shell.update (Shell.Editor(Editor.Answered("my-post", Ok post))) started)

let private draft = document Api.Draft (Some "\"v1\"") noDetails

let private withSheet (post: Api.PostDocument) =
  let model, _ = Shell.update (Shell.Editor(Editor.DetailsMsg Details.Opened)) (opened post)
  fst (Shell.update (Shell.Editor(Editor.DetailsMsg(Details.OptionsAnswered(Ok choices)))) model)

let private send msg (model: Shell.Model) = Shell.update (Shell.Editor msg) model
let private detail msg = send (Editor.DetailsMsg msg)

let private document' (model: Shell.Model) =
  match model.Editor with
  | Some(Editor.Loaded document) -> document
  | other -> failtestf "Expected a loaded editor, got %A" other

let private values (model: Shell.Model) = (document' model).Details.Values

let private saved (model: Shell.Model) =
  match send Editor.SaveRequested model with
  | _, Some(Shell.Send [ Shell.SavePost request ]) -> request
  | _, effect -> failtestf "Expected a save, got %A" effect

[<Tests>]
let tests =
  testList "Details" [
    test "the term slug has the rule of the build" {
      for text in
        [ "Functional programming"
          "functional Programming"
          "F#"
          "C# & F#"
          "Café Crème"
          "Don't panic"
          "  --Spaced out--  "
          "snake_case term"
          "100% sure!"
          "日本語" ] do
        Expect.equal (Details.termSlug text) (TwoPoint.Site.Render.Slug.term text) $"The slug of '{text}'"
    }

    test "the term title has the rule of the build" {
      for text in [ "FSharp"; "fsharp tips"; "CSharp"; "Testing" ] do
        Expect.equal (Details.termTitle text) (TwoPoint.Site.Render.Slug.termTitle text) $"The title of '{text}'"
    }

    test "a post slug has lowercase letters and digits with a single dash between them" {
      Expect.isTrue (Details.isSlug "my-post-2") "A slug"
      Expect.isFalse (Details.isSlug "My-post") "Capital"
      Expect.isFalse (Details.isSlug "my--post") "Double dash"
      Expect.isFalse (Details.isSlug "-my-post") "Leading dash"
      Expect.isFalse (Details.isSlug "") "Empty"
    }

    test "a typed term gives the title of the known term with the same slug" {
      Expect.equal (Details.resolve choices.Terms.Tags "functional Programming") (Some "Functional programming") "Known"
      Expect.equal (Details.resolve choices.Terms.Tags " Brand new ") (Some "Brand new") "New"
      Expect.equal (Details.resolve choices.Terms.Tags "!!!") None "No letter"
    }

    test "the suggestions match by slug, skip the terms of the post, and keep the order of the list" {
      let names (terms: Api.Term list) = terms |> List.map _.Title
      Expect.equal (names (Details.suggestions choices.Terms.Tags "FUNCTION" [])) [ "Functional programming" ] "Match"
      Expect.equal (names (Details.suggestions choices.Terms.Tags "" [ "FSharp" ])) [ "Functional programming"; "Testing" ] "Skip"
    }

    test "the first open of the sheet loads the suggestions, and a later open does not" {
      let model, effect = detail Details.Opened (opened draft)
      Expect.isTrue (document' model).Details.Open "Open"
      Expect.equal (document' model).Details.Options Details.Waiting "Waiting"
      Expect.equal effect (Some(Shell.Send [ Shell.LoadDetailsOptions ])) "The call"

      let model, _ = detail (Details.OptionsAnswered(Ok choices)) model
      let model, _ = detail Details.Closed model
      Expect.isFalse (document' model).Details.Open "Closed"
      let model, effect = detail Details.Opened model
      Expect.isNone effect "No second call"
      Expect.equal (document' model).Details.Options (Details.Ready choices) "The suggestions stay"
    }

    test "a failed load of the suggestions shows the reason, and the next open tries again" {
      let model, _ = detail Details.Opened (opened draft)
      let model, _ = detail (Details.OptionsAnswered(Error Api.Unanswered)) model
      Expect.equal (document' model).Details.Options (Details.NotLoaded "The API did not answer.") "The reason"
      let model, _ = detail Details.Closed model
      let _, effect = detail Details.Opened model
      Expect.equal effect (Some(Shell.Send [ Shell.LoadDetailsOptions ])) "Again"
    }

    test "a new draft has no category" {
      Expect.equal (values (opened draft)).Category None "No category"
    }

    test "a typed tag gives the existing tag with the same slug, and the save has it" {
      let model, effect = detail (Details.TagAdded "functional Programming") (withSheet draft)
      Expect.equal (values model).Tags [ "Functional programming" ] "The existing title"
      Expect.equal effect (Some(Shell.Autosave 1)) "The autosave timer"
      Expect.equal (saved model).Details.Tags [ "Functional programming" ] "The save"
    }

    test "a typed tag that is new keeps the typed text, and a tag with a known slug is not added twice" {
      let model, _ = detail (Details.TagAdded "Brand new") (withSheet draft)
      let model, _ = detail (Details.TagAdded "BRAND-new") model
      Expect.equal (values model).Tags [ "Brand new" ] "One tag"
      Expect.equal (document' model).Version 1 "The second add changes nothing"
    }

    test "a tag goes by its slug" {
      let model, _ = detail (Details.TagAdded "Brand new") (withSheet draft)
      let model, _ = detail (Details.TagRemoved "brand-new") model
      Expect.equal (values model).Tags [] "No tags"
    }

    test "a typed category gives the existing category with the same slug" {
      let model, _ = detail (Details.CategoryChosen "dEV") (withSheet draft)
      Expect.equal (values model).Category (Some "Dev") "The existing title"
    }

    test "the category Series is not a choice of the category picker" {
      let model, effect = detail (Details.CategoryChosen "series") (withSheet draft)
      Expect.equal (values model).Category None "No category"
      Expect.isNone effect "No timer"
    }

    test "a series sets the category Series and locks the category picker" {
      let model, _ = detail (Details.SeriesChosen "my-series") (withSheet draft)
      Expect.equal (values model).Series (Some "my-series") "The series"
      Expect.equal (values model).Category (Some "Series") "The category"

      let model, effect = detail (Details.CategoryChosen "Dev") model
      Expect.isNone effect "The picker is locked"
      Expect.equal (values model).Category (Some "Series") "The category stays"
      let model, _ = detail Details.CategoryCleared model
      Expect.equal (values model).Category (Some "Series") "The category stays after a clear"

      let request = saved model
      Expect.equal request.Details.Series (Some "my-series") "The save has the series"
      Expect.equal request.Details.Category (Some "Series") "The save has the category"
    }

    test "the removal of the series leaves the post with no category and frees the picker" {
      let post = document Api.Draft (Some "\"v1\"") { noDetails with Series = Some "my-series"; Category = Some "Series" }
      let model, effect = detail Details.SeriesRemoved (withSheet post)
      Expect.equal (values model).Series None "No series"
      Expect.equal (values model).Category None "No category"
      Expect.equal effect (Some(Shell.Autosave 1)) "The autosave timer"

      let model, _ = detail (Details.CategoryChosen "Life") model
      Expect.equal (values model).Category (Some "Life") "The picker works again"
    }

    test "the author, the image, and the home page flag change the details, and a blank text clears a field" {
      let model, _ = detail (Details.AuthorChanged "Matt") (withSheet draft)
      let model, _ = detail (Details.ImageChanged "https://x.dev/a.png") model
      let model, _ = detail (Details.ShowOnHomepageChanged false) model

      Expect.equal
        (values model)
        { noDetails with Author = Some "Matt"; Image = Some "https://x.dev/a.png"; ShowOnHomepage = false }
        "The details"

      let model, _ = detail (Details.ImageChanged "  ") model
      Expect.equal (values model).Image None "Cleared"
    }

    test "a change of the sheet that changes no value starts no save" {
      let _, effect = detail (Details.CategoryTyped "de") (withSheet draft)
      Expect.isNone effect "No timer"
    }

    test "the slug of a saved draft changes with the ETag, and the author goes to the new address" {
      let model, _ = detail (Details.SlugTyped "new-slug") (withSheet draft)
      let model, effect = send Editor.SlugChangeRequested model

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.ChangeSlug { Slug = "my-post"; ETag = "\"v1\""; NewSlug = "new-slug" } ]))
        "The call"

      Expect.equal (document' model).Details.Slug Details.Changing "Changing"

      let model, effect =
        send (Editor.SlugChangeAnswered("my-post", Ok { Slug = "new-slug"; ETag = "\"v2\"" })) model

      Expect.equal
        effect
        (Some(Shell.Navigate(Router.PushUrl "/posts/new-slug", [ Shell.LoadPost "new-slug" ])))
        "The new address"

      Expect.equal model.Router.Route (Route.Post "new-slug") "The route"
    }

    test "a slug that is not a slug starts no call" {
      let model, _ = detail (Details.SlugTyped "Not A Slug") (withSheet draft)
      let model, effect = send Editor.SlugChangeRequested model
      Expect.isNone effect "No call"
      Expect.isFalse (Details.slugChangeable (document' model).Slug (document' model).Details) "Not changeable"
    }

    test "the slug of a published post is fixed" {
      let post = document Api.Published None noDetails
      let model, _ = detail (Details.SlugTyped "new-slug") (withSheet post)
      let model, effect = send Editor.SlugChangeRequested model
      Expect.isNone effect "No call"

      Expect.equal
        (document' model).Details.Slug
        (Details.ChangeFailed "The slug of a published post is fixed.")
        "The reason"
    }

    test "the slug of a draft with unsaved text waits for the save" {
      let model, _ = send (Editor.BodyChanged "x") (withSheet draft)
      let model, _ = detail (Details.SlugTyped "new-slug") model
      let model, effect = send Editor.SlugChangeRequested model
      Expect.isNone effect "No call"

      Expect.equal
        (document' model).Details.Slug
        (Details.ChangeFailed "Wait until the post is saved, and then change the slug.")
        "The reason"
    }

    test "a refused slug change shows the reason from the API and keeps the author on the editor" {
      let model, _ = detail (Details.SlugTyped "taken") (withSheet draft)
      let model, _ = send Editor.SlugChangeRequested model
      let reason = "Another post has the slug 'taken'"
      let model, effect = send (Editor.SlugChangeAnswered("my-post", Error(Api.Failed reason))) model
      Expect.isNone effect "No navigation"
      Expect.equal (document' model).Details.Slug (Details.ChangeFailed reason) "The reason"
      Expect.equal model.Router.Route (Route.Post "my-post") "The route"

      let model, _ = detail (Details.SlugTyped "other") model
      Expect.equal (document' model).Details.Slug Details.Idle "A new text clears the reason"
    }

    test "a stale slug change stops the saves of the page" {
      let model, _ = detail (Details.SlugTyped "new-slug") (withSheet draft)
      let model, _ = send Editor.SlugChangeRequested model
      let model, _ = send (Editor.SlugChangeAnswered("my-post", Error Api.Stale)) model
      Expect.equal (document' model).Save Editor.Stale "Stale"
      Expect.equal (document' model).Details.Slug Details.Idle "Idle"
    }

    test "text typed during a slug change is saved under the new slug before the author goes there" {
      let model, _ = detail (Details.SlugTyped "new-slug") (withSheet draft)
      let model, _ = send Editor.SlugChangeRequested model
      let model, _ = send (Editor.BodyChanged "x") model
      let model, effect = send (Editor.AutosaveElapsed 1) model
      Expect.isNone effect "No save under the old slug"

      let model, effect =
        send (Editor.SlugChangeAnswered("my-post", Ok { Slug = "new-slug"; ETag = "\"v2\"" })) model

      match effect with
      | Some(Shell.Send [ Shell.SavePost request ]) ->
        Expect.equal request.Slug "new-slug" "The new slug"
        Expect.equal request.ETag (Some "\"v2\"") "The new ETag"
        Expect.equal request.Fields.Body "x" "The text"
      | other -> failtestf "Expected a save, got %A" other

      Expect.equal model.Router.Route (Route.Post "my-post") "The author stays until the save"

      let model, effect = send (Editor.SaveAnswered("new-slug", 1, Ok(savedAs "\"v3\""))) model

      Expect.equal
        effect
        (Some(Shell.Navigate(Router.PushUrl "/posts/new-slug", [ Shell.LoadPost "new-slug" ])))
        "Now the author goes"

      Expect.equal model.Router.Route (Route.Post "new-slug") "The route"
    }

    test "a refused token on a slug change offers another account" {
      let model, _ = detail (Details.SlugTyped "new-slug") (withSheet draft)
      let model, _ = send Editor.SlugChangeRequested model
      let model, _ = send (Editor.SlugChangeAnswered("my-post", Error Api.Refused)) model
      Expect.equal model.Session.Status Session.Refused "The banner"
    }
  ]
