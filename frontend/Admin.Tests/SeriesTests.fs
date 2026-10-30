module TwoPoint.Admin.Tests.SeriesTests

open TwoPoint.Admin

open Expecto

let private post slug title state publishDate : Api.SeriesPost =
  { Slug = slug
    Title = title
    State = state
    PublishDate = publishDate }

let private record slug title posts : Api.SeriesRecord =
  { Slug = slug
    Title = title
    Excerpt = $"Posts about {title}"
    Image = None
    ETag = $"\"{slug}-1\""
    Posts = posts }

let private fable =
  record
    "fable"
    "Fable"
    [ post "zeta-draft" "Zeta draft" Api.SeriesPostState.Draft None
      post "upcoming" "Upcoming" Api.SeriesPostState.Upcoming (Some "2026-12-01T00:00:00Z")
      post "alpha-edit" "Alpha edit" Api.SeriesPostState.PendingEdit None
      post "first" "First" Api.SeriesPostState.Published (Some "2026-01-01T00:00:00Z")
      post "alpha-draft" "Alpha draft" Api.SeriesPostState.Draft None ]

let private drafts =
  record "notes" "Notes" [ post "a-draft" "A draft" Api.SeriesPostState.Draft None ]

let private empty = record "empty" "Empty" []

let private start () =
  let model, _ = Shell.init "/series"
  Shell.update (Shell.Session(Session.Started true)) model

let private listed () =
  let model, _ = start ()
  let model, _ = Shell.update (Shell.Series(Series.Answered(Ok [ fable; drafts; empty ]))) model
  model

let private send msg model = Shell.update (Shell.Series msg) model

let private sendAll msgs model =
  msgs |> List.fold (fun (model, _) msg -> send msg model) (model, None)

let private form (model: Shell.Model) =
  match model.Series.Form with
  | Some form -> form
  | None -> failtest "Expected an open form"

let private problems (model: Shell.Model) =
  Series.problems model.Series (form model) |> List.map fst

[<Tests>]
let tests =
  testList "Series page" [
    test "a start on the series page loads the series and the deploy state" {
      let _, effect = start ()

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.LoadDashboard; Shell.LoadQueue; Shell.LoadSeries; Shell.LoadSiteDeploy ]))
        "The API calls"
    }

    test "the posts of a series are in publish date order, an upcoming post too, and the drafts after them" {
      Expect.equal
        (Series.orderedPosts fable |> List.map _.Slug)
        [ "first"; "upcoming"; "alpha-draft"; "zeta-draft"; "alpha-edit" ]
        "The live posts by date, then the drafts, then the pending edits"
    }

    test "a new series gives its slug from the title until the author types a slug" {
      let model, _ = listed () |> sendAll [ Series.NewClicked; Series.TitleTyped "Matt's F# Notes & Tips" ]
      Expect.equal (form model).Slug "matts-f-notes-and-tips" "The term slug of the title"

      let model, _ = model |> sendAll [ Series.SlugTyped "fsharp-notes"; Series.TitleTyped "Other title" ]
      Expect.equal (form model).Slug "fsharp-notes" "The typed slug stays"
    }

    test "a new form shows no empty field as a problem before the first save" {
      let model, _ = listed () |> send Series.NewClicked
      Expect.isEmpty (problems model) "No problem yet"

      let model, effect = model |> send Series.SaveClicked
      Expect.isNone effect "No save"
      Expect.equal (problems model) [ Series.EmptyTitle; Series.BadSlug; Series.EmptyExcerpt ] "Each empty field"
      Expect.equal (Series.fieldProblem model.Series Series.TitleField) (Some "Give the series a title.") "The title text"
    }

    test "a bad slug, a slug of another series, and an image that is not https are problems at once" {
      let model, _ =
        listed ()
        |> sendAll [ Series.NewClicked; Series.TitleTyped "Notes"; Series.ImageTyped "http://example.com/a.jpg" ]

      Expect.equal (problems model) [ Series.SlugTaken; Series.BadImage ] "The slug of Notes and the image"

      let model, _ = model |> send (Series.SlugTyped "Bad Slug")
      Expect.equal (problems model) [ Series.BadSlug; Series.BadImage ] "The bad slug"
      Expect.equal (Series.fieldProblem model.Series Series.SlugField) (Some(Series.problemText Series.BadSlug)) "On the slug"
    }

    test "the series keeps its own slug in an edit" {
      let model, _ = listed () |> send (Series.Opened "notes")
      Expect.isEmpty (problems model) "Its own slug is not taken"
    }

    test "a save with no problem sends the fields, and its answer reloads the list and polls the deploy" {
      let model, _ = listed () |> sendAll [ Series.Opened "empty"; Series.TitleTyped " Empty, again "; Series.SlugTyped "empty-again" ]
      let model, effect = model |> send Series.SaveClicked

      Expect.equal
        effect
        (Some(
          Shell.Send
            [ Shell.SaveSeries
                { Slug = "empty"
                  ETag = Some "\"empty-1\""
                  Fields =
                    { Slug = "empty-again"
                      Title = "Empty, again"
                      Excerpt = "Posts about Empty"
                      Image = None } } ]
        ))
        "The save"

      let saved: Api.SavedSeries = { Slug = "empty-again"; ETag = "\"empty-2\""; Warnings = [] }
      let model, effect = model |> send (Series.SaveAnswered(Ok saved))

      Expect.isNone model.Series.Form "The form closes"
      Expect.equal effect (Some(Shell.Send [ Shell.LoadSeries; Shell.PollSiteDeploy model.SiteDeploy.Generation ])) "Reload and poll"
      Expect.equal (Series.noticeText model.Series.Notice) (Some "The series 'empty-again' is saved, and the site rebuilds.") "The notice"
    }

    test "the slug is locked while a published post, a draft, or a pending edit refers to the series" {
      let model, _ = listed () |> sendAll [ Series.Opened "fable"; Series.SlugTyped "other" ]
      Expect.isTrue (Series.slugLocked (form model)) "Locked"
      Expect.equal (form model).Slug "fable" "The slug stays"

      let model, _ = listed () |> sendAll [ Series.Opened "notes"; Series.SlugTyped "other" ]
      Expect.isTrue (Series.slugLocked (form model)) "A draft locks the slug"
      Expect.equal (form model).Slug "notes" "The slug stays"

      let model, _ = listed () |> sendAll [ Series.Opened "empty"; Series.SlugTyped "other" ]
      Expect.isFalse (Series.slugLocked (form model)) "No post, no lock"
      Expect.equal (form model).Slug "other" "The slug changes"
    }

    test "a series with a post cannot be deleted, and the form gives the reason" {
      let model, _ = listed () |> send (Series.Opened "notes")
      Expect.isFalse (Series.canDelete (form model)) "No delete"

      Expect.equal
        (Series.deleteReason (form model))
        (Some "The series has 1 draft. Remove the series from each post first, and then delete the series.")
        "The reason"

      let model, _ = listed () |> send (Series.Opened "fable")

      Expect.equal
        (Series.deleteReason (form model))
        (Some
          "The series has 1 published post, 1 upcoming post, 2 drafts and 1 pending edit. Remove the series from each post first, and then delete the series.")
        "Each kind of post, with its count"

      let model, _ = Shell.update (Shell.Opened Shell.DeleteSeriesDialog) model
      Expect.isNone (Shell.deleteSeriesChosen model) "The dialog does not confirm a delete"
    }

    test "a delete of a series with no post comes only after the confirmation" {
      let model, _ = listed () |> send (Series.Opened "empty")
      let _, effect = model |> send Series.DeleteChosen
      Expect.isNone effect "No dialog, no delete"

      let model, _ = Shell.update (Shell.Opened Shell.DeleteSeriesDialog) model

      match Shell.deleteSeriesChosen model with
      | Some msg ->
        let model, effect = Shell.update msg model
        Expect.equal effect (Some(Shell.Send [ Shell.DeleteSeries("empty", "\"empty-1\"") ])) "The delete"

        let model, _ = model |> send (Series.DeleteAnswered("empty", Ok()))
        Expect.isNone model.Series.Form "The form closes"

        match model.Series.Records with
        | Series.Loaded records -> Expect.equal (records |> List.map _.Slug) [ "fable"; "notes" ] "The series goes away"
        | other -> failtestf "Expected the list, got %A" other
      | None -> failtest "Expected the confirmation"
    }

    test "an uploaded image fills the image field" {
      let model, _ = listed () |> send (Series.Opened "empty")
      let model, effect = model |> send (Series.ImagePicked Unchecked.defaultof<Browser.Types.File>)
      Expect.isTrue (Series.isUploading (form model)) "The upload starts"
      Expect.isSome effect "The upload call"

      let model, _ = model |> send (Series.ImageUploaded(Ok "https://twopointwebsite.blob.core.windows.net/assets/a.jpg"))

      Expect.equal (form model).Image "https://twopointwebsite.blob.core.windows.net/assets/a.jpg" "The URL"
      Expect.equal (form model).Upload Series.NoUpload "The upload is done"
    }

    test "a stale save keeps the form open with the reason" {
      let model, _ = listed () |> sendAll [ Series.Opened "notes"; Series.SaveClicked; Series.SaveAnswered(Error Api.Stale) ]
      Expect.stringContains (Series.failure (form model) |> Option.defaultValue "") "changed in another place" "The reason"
    }
  ]
