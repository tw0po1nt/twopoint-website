module TwoPoint.Admin.Tests.EditorTests

open TwoPoint.Admin

open Expecto

let private noDetails: Api.PostDetails =
  { Author = None
    Category = None
    Tags = []
    Series = None
    Image = None
    ShowOnHomepage = true }

let private document state pendingEdit publishDate eTag : Api.PostDocument =
  { Slug = "my-post"
    State = state
    PendingEdit = pendingEdit
    PublishDate = publishDate
    ETag = eTag
    Title = "My post"
    Excerpt = "About it."
    Body = "Some text.\n"
    Details = noDetails
    Text = ""
    PublishedText = None }

let private draft = document Api.Draft false None (Some "\"v1\"")
let private live = document Api.Published false (Some "2026-02-04T13:00:00Z") None

let private started (path: string) =
  let model, _ = Shell.init path
  fst (Shell.update (Shell.Session(Session.Started true)) model)

let private opened (post: Api.PostDocument) =
  let model = started "/posts/my-post"
  fst (Shell.update (Shell.Editor(Editor.Answered("my-post", Ok post))) model)

let private send msg (model: Shell.Model) = Shell.update (Shell.Editor msg) model

let private document' (model: Shell.Model) =
  match model.Editor with
  | Some(Editor.Loaded document) -> document
  | other -> failtestf "Expected a loaded editor, got %A" other

let private status (model: Shell.Model) = Editor.statusText (document' model)

let private request eTag title excerpt body version : Editor.SaveRequest =
  { Slug = "my-post"
    ETag = eTag
    Fields = { Title = title; Excerpt = excerpt; Body = body }
    Details = noDetails
    Version = version }

[<Tests>]
let tests =
  testList "Editor" [
    test "a start on a post loads the post with the first API calls" {
      let model, effect = Shell.init "/posts/my-post"
      Expect.equal model.Editor (Some(Editor.Loading "my-post")) "The editor waits"
      let _, effect' = Shell.update (Shell.Session(Session.Started true)) model
      Expect.equal effect (Some(Shell.SignIn Session.Start)) "No call before the sign-in"

      Expect.equal
        effect'
        (Some(Shell.Send [ Shell.LoadDashboard; Shell.LoadQueue; Shell.LoadPost "my-post" ]))
        "The post"
    }

    test "a loaded draft shows its fields and the status text Draft · saved" {
      let model = opened draft
      Expect.equal (document' model).Fields { Title = "My post"; Excerpt = "About it."; Body = "Some text.\n" } "The fields"
      Expect.equal (status model) "Draft · saved" "The status"
    }

    test "a loaded published post shows its publish date, and a pending edit shows that the changes are not live" {
      Expect.equal (status (opened live)) "Published Feb 4, 2026" "Published"

      let edited = document Api.Published true (Some "2026-02-04T13:00:00Z") (Some "\"v3\"")
      Expect.equal (status (opened edited)) "Published Feb 4, 2026 · changes not live · saved" "Pending edit"

      let upcoming = document Api.Upcoming false (Some "2099-12-31T00:00:00Z") None
      Expect.equal (status (opened upcoming)) "Upcoming Dec 31, 2099" "Upcoming"
    }

    test "a post that does not load shows the reason" {
      let model = started "/posts/missing"
      let model, _ = send (Editor.Answered("missing", Error(Api.Failed "Post 'missing' not found"))) model
      Expect.equal model.Editor (Some(Editor.NotLoaded "Post 'missing' not found")) "The reason"
    }

    test "a typed letter marks the post unsaved and starts the autosave timer" {
      let model, effect = send (Editor.BodyChanged "Some text.\nMore") (opened draft)
      Expect.equal effect (Some(Shell.Autosave 1)) "The timer for version 1"
      Expect.equal (document' model).Fields.Body "Some text.\nMore" "The text"
      Expect.equal (status model) "Draft · unsaved changes" "The status"
      Expect.equal Editor.autosaveDelay 2000 "Two seconds"
    }

    test "the timer of an older input saves nothing, and the timer of the last input saves" {
      let model, _ = send (Editor.TitleChanged "A") (opened draft)
      let model, _ = send (Editor.TitleChanged "AB") model
      let model, early = send (Editor.AutosaveElapsed 1) model
      Expect.isNone early "The first timer is too early"
      Expect.equal (status model) "Draft · unsaved changes" "Still unsaved"

      let model, effect = send (Editor.AutosaveElapsed 2) model

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.SavePost(request (Some "\"v1\"") "AB" "About it." "Some text.\n" 2) ]))
        "The save"

      Expect.equal (status model) "Draft · saving…" "The status"
    }

    test "a save answer shows saved and keeps the new ETag for the next save" {
      let model, _ = send (Editor.ExcerptChanged "New excerpt") (opened draft)
      let model, _ = send (Editor.AutosaveElapsed 1) model
      let model, effect = send (Editor.SaveAnswered("my-post", 1, Ok "\"v2\"")) model
      Expect.isNone effect "Nothing more to save"
      Expect.equal (status model) "Draft · saved" "Saved"

      let model, _ = send (Editor.BodyChanged "x") model
      let _, effect = send (Editor.AutosaveElapsed 2) model
      Expect.equal (effect |> Option.map (function Shell.Send [ Shell.SavePost request ] -> request.ETag | _ -> None)) (Some(Some "\"v2\"")) "The new ETag"
    }

    test "the keys Command and S save at once, with no timer" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, effect = send Editor.SaveRequested model
      Expect.equal effect (Some(Shell.Send [ Shell.SavePost(request (Some "\"v1\"") "My post" "About it." "x" 1) ])) "The save"
      Expect.equal (status model) "Draft · saving…" "The status"
    }

    test "the keys Command and S with no change save nothing" {
      let _, effect = send Editor.SaveRequested (opened draft)
      Expect.isNone effect "Nothing to save"
    }

    test "the timer after a save that already ran saves nothing" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, _ = send Editor.SaveRequested model
      let model, effect = send (Editor.AutosaveElapsed 1) model
      Expect.isNone effect "The save is in flight"
      Expect.equal (status model) "Draft · saving…" "Still saving"
    }

    test "text typed during a save is unsaved after the answer, and a new timer starts" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, _ = send Editor.SaveRequested model
      let model, _ = send (Editor.BodyChanged "xy") model
      let model, effect = send (Editor.SaveAnswered("my-post", 1, Ok "\"v2\"")) model
      Expect.equal effect (Some(Shell.Autosave 2)) "A timer for the last input"
      Expect.equal (status model) "Draft · unsaved changes" "Unsaved"
      Expect.equal (document' model).Fields.Body "xy" "The text stays"
    }

    test "Command and S during a save saves again as soon as the answer comes" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, _ = send Editor.SaveRequested model
      let model, _ = send (Editor.BodyChanged "xy") model
      let model, none = send Editor.SaveRequested model
      Expect.isNone none "One save at a time"
      let _, effect = send (Editor.SaveAnswered("my-post", 1, Ok "\"v2\"")) model
      Expect.equal effect (Some(Shell.Send [ Shell.SavePost(request (Some "\"v2\"") "My post" "About it." "xy" 2) ])) "The second save"
    }

    test "a failed save keeps the text, shows the failure, and tries again after the delay" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, _ = send (Editor.AutosaveElapsed 1) model
      let model, effect = send (Editor.SaveAnswered("my-post", 1, Error Api.Unanswered)) model
      Expect.equal (status model) "Draft · save failed" "The status"
      Expect.equal (document' model).Fields.Body "x" "The text stays"
      Expect.equal effect (Some(Shell.Autosave 1)) "A new timer"

      let model, effect = send (Editor.AutosaveElapsed 1) model
      Expect.isSome effect "The second try"
      Expect.equal (status model) "Draft · saving…" "Saving again"
    }

    test "a save from an old tab fails with a stale answer, keeps the text, and tries nothing more" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, _ = send (Editor.AutosaveElapsed 1) model
      let model, effect = send (Editor.SaveAnswered("my-post", 1, Error Api.Stale)) model
      Expect.isNone effect "No retry"
      Expect.equal (status model) "Draft · changed in another tab" "The status"
      Expect.equal (document' model).Fields.Body "x" "The text stays"

      let model, effect = send (Editor.BodyChanged "xy") model
      Expect.isNone effect "No timer"
      let _, effect = send Editor.SaveRequested model
      Expect.isNone effect "No save"
    }

    test "a refused token on a save offers another account and keeps the text" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, _ = send (Editor.AutosaveElapsed 1) model
      let model, _ = send (Editor.SaveAnswered("my-post", 1, Error Api.Refused)) model
      Expect.equal model.Session.Status Session.Refused "The banner"
      Expect.equal (document' model).Fields.Body "x" "The text stays"
    }

    test "the first save of a published post has no ETag and makes a pending edit" {
      let model, _ = send (Editor.TitleChanged "Edited") (opened live)
      Expect.equal (status model) "Published Feb 4, 2026 · changes not live · unsaved changes" "Unsaved"
      let model, effect = send Editor.SaveRequested model

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.SavePost(request None "Edited" "About it." "Some text.\n" 1) ]))
        "No ETag"

      Expect.equal (status model) "Published Feb 4, 2026 · changes not live · saving…" "Saving"
      let model, _ = send (Editor.SaveAnswered("my-post", 1, Ok "\"w1\"")) model
      Expect.equal (status model) "Published Feb 4, 2026 · changes not live · saved" "Saved"
      Expect.isTrue (document' model).PendingEdit "A pending edit"
    }

    test "an interaction error during a save keeps the text and shows the Sign in again banner" {
      let model, _ = send (Editor.BodyChanged "Words that I typed") (opened draft)
      let model, effect = send (Editor.AutosaveElapsed 1) model
      let call = effect |> Option.bind (function Shell.Send [ call ] -> Some call | _ -> None) |> Option.get

      let model, _ = Shell.update (Shell.Session(Session.InteractionRequired call)) model

      Expect.equal model.Session.Status (Session.Expired false) "Expired"
      Expect.isTrue (Session.showsBanner model.Session) "The banner shows"
      Expect.equal (Session.bannerText model.Session) "Your session ended. Sign in again to continue." "The text"
      Expect.equal (document' model).Fields.Body "Words that I typed" "The text stays"
      Expect.equal model.Session.Waiting [ call ] "The save waits"

      let model, _ = Shell.update (Shell.Session Session.SignInAgainClicked) model
      let model, effect = Shell.update (Shell.Session Session.Renewed) model
      Expect.equal effect (Some(Shell.SignIn(Session.Retry [ call ]))) "The save runs again"
      Expect.equal (document' model).Fields.Body "Words that I typed" "The text still stays"
    }

    test "the top bar fades while the author types, and comes back at a move of the pointer" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      Expect.isTrue (document' model).Typing "Faded"
      let model, _ = send Editor.PointerMoved model
      Expect.isFalse (document' model).Typing "Back"
    }

    test "the author who leaves the editor with unsaved text stays until the API has the text" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, effect = Shell.update (Shell.Router(Router.LinkFollowed "/posts")) model

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.SavePost(request (Some "\"v1\"") "My post" "About it." "x" 1) ]))
        "The save"

      Expect.equal model.Router.Route (Route.Post "my-post") "The author stays on the editor"
      Expect.equal (document' model).Fields.Body "x" "The text stays"

      let model, effect = send (Editor.SaveAnswered("my-post", 1, Ok "\"v2\"")) model
      Expect.equal effect (Some(Shell.Navigate(Router.PushUrl "/posts", [ Shell.LoadPosts ]))) "Now the author leaves"
      Expect.equal model.Router.Route Route.Posts "The post list"
      Expect.isNone model.Editor "The editor goes"
    }

    test "text typed during a save is saved before the author leaves" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, _ = send Editor.SaveRequested model
      let model, _ = send (Editor.BodyChanged "xy") model
      let model, effect = Shell.update (Shell.Router(Router.LinkFollowed "/posts")) model
      Expect.isNone effect "One save at a time"
      Expect.equal model.Router.Route (Route.Post "my-post") "The author stays"

      let model, effect = send (Editor.SaveAnswered("my-post", 1, Ok "\"v2\"")) model

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.SavePost(request (Some "\"v2\"") "My post" "About it." "xy" 2) ]))
        "The newest text"

      Expect.equal model.Router.Route (Route.Post "my-post") "The author still stays"
      let model, effect = send (Editor.SaveAnswered("my-post", 2, Ok "\"v3\"")) model
      Expect.equal effect (Some(Shell.Navigate(Router.PushUrl "/posts", [ Shell.LoadPosts ]))) "Now the author leaves"
      Expect.equal model.Router.Route Route.Posts "The post list"
    }

    test "a failed save after a leave keeps the author on the editor with the text" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, _ = Shell.update (Shell.Router(Router.LinkFollowed "/posts")) model
      let model, _ = send (Editor.SaveAnswered("my-post", 1, Error Api.Unanswered)) model
      Expect.equal model.Router.Route (Route.Post "my-post") "The author stays"
      Expect.equal (document' model).Fields.Body "x" "The text stays"
      Expect.equal (document' model).LeaveTo None "No leave waits"
      Expect.equal (status model) "Draft · save failed" "The status"
    }

    test "a stale answer after a leave keeps the author on the editor, and a second leave goes" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, _ = Shell.update (Shell.Router(Router.LinkFollowed "/posts")) model
      let model, _ = send (Editor.SaveAnswered("my-post", 1, Error Api.Stale)) model
      Expect.equal model.Router.Route (Route.Post "my-post") "The author stays"
      Expect.equal (document' model).Fields.Body "x" "The text stays"
      Expect.equal (status model) "Draft · changed in another tab" "The status"

      let model, _ = Shell.update (Shell.Router(Router.LinkFollowed "/posts")) model
      Expect.equal model.Router.Route Route.Posts "The second leave goes"
    }

    test "a leave after a session expiry saves the newest text after the waiting save" {
      let model, _ = send (Editor.BodyChanged "a") (opened draft)
      let model, effect = send (Editor.AutosaveElapsed 1) model
      let call = effect |> Option.bind (function Shell.Send [ call ] -> Some call | _ -> None) |> Option.get
      let model, _ = Shell.update (Shell.Session(Session.InteractionRequired call)) model
      let model, _ = send (Editor.BodyChanged "ab") model
      let model, _ = Shell.update (Shell.Router(Router.LinkFollowed "/posts")) model
      Expect.equal model.Router.Route (Route.Post "my-post") "The author stays"

      let model, _ = Shell.update (Shell.Session Session.SignInAgainClicked) model
      let model, effect = Shell.update (Shell.Session Session.Renewed) model
      Expect.equal effect (Some(Shell.SignIn(Session.Retry [ call ]))) "The waiting save runs"

      let model, effect = send (Editor.SaveAnswered("my-post", 1, Ok "\"v2\"")) model

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.SavePost(request (Some "\"v2\"") "My post" "About it." "ab" 2) ]))
        "The newest text"

      let model, _ = send (Editor.SaveAnswered("my-post", 2, Ok "\"v3\"")) model
      Expect.equal model.Router.Route Route.Posts "The author leaves"
    }

    test "the Back button of the browser with unsaved text puts the editor address back and saves" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, effect = Shell.update (Shell.Router(Router.UrlChanged "/posts")) model

      Expect.equal
        effect
        (Some(
          Shell.Navigate(
            Router.PushUrl "/posts/my-post",
            [ Shell.SavePost(request (Some "\"v1\"") "My post" "About it." "x" 1) ]
          )
        ))
        "The address and the save"

      Expect.equal model.Router.Route (Route.Post "my-post") "The author stays"
    }

    test "the author who leaves the editor with saved text sends no save" {
      let _, effect = Shell.update (Shell.Router(Router.LinkFollowed "/posts")) (opened draft)

      match effect with
      | Some(Shell.Navigate(_, calls)) -> Expect.equal calls [ Shell.LoadPosts ] "Only the posts"
      | other -> failtestf "Expected a navigation, got %A" other
    }

    test "an answer for another post changes nothing" {
      let model = opened draft
      let after, _ = send (Editor.SaveAnswered("other", 1, Ok "\"x\"")) model
      Expect.equal after model "No change"
    }

    test "the primary action says Publish for a draft and Publish changes for a published post" {
      Expect.equal (Editor.primaryAction (document' (opened draft))) "Publish" "Draft"
      Expect.equal (Editor.primaryAction (document' (opened live))) "Publish changes" "Published"
    }
  ]

[<Tests>]
let draftTests =
  testList "New draft" [
    test "New draft on the post list sends the create call once" {
      let model = started "/posts"
      let model, effect = Shell.update (Shell.Posts Posts.NewDraftClicked) model
      Expect.equal effect (Some(Shell.Send [ Shell.CreateDraft ])) "The create call"
      let _, again = Shell.update (Shell.Posts Posts.NewDraftClicked) model
      Expect.isNone again "One call at a time"
    }

    test "a created draft opens in the editor" {
      let model = started "/posts"
      let model, _ = Shell.update (Shell.Posts Posts.NewDraftClicked) model
      let created : Api.CreatedDraft = { Slug = "untitled"; ETag = "\"v1\"" }
      let model, effect = Shell.update (Shell.Posts(Posts.DraftCreated(Ok created))) model

      Expect.equal model.Router.Route (Route.Post "untitled") "The editor"
      Expect.equal model.Editor (Some(Editor.Loading "untitled")) "The editor loads"
      Expect.equal effect (Some(Shell.Navigate(Router.PushUrl "/posts/untitled", [ Shell.LoadPost "untitled" ]))) "The call"
    }

    test "a failed create shows the reason and lets the author try again" {
      let model = started "/posts"
      let model, _ = Shell.update (Shell.Posts Posts.NewDraftClicked) model
      let model, _ = Shell.update (Shell.Posts(Posts.DraftCreated(Error(Api.Failed "No."))) ) model
      Expect.equal model.Posts.CreateFailure (Some "No.") "The reason"
      let _, effect = Shell.update (Shell.Posts Posts.NewDraftClicked) model
      Expect.isSome effect "Another try"
    }
  ]
