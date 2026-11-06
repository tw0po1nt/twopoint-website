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
    DeliveryStarted = None
    Text = ""
    Published = None }

let private savedAs eTag : Api.SavedPost = { ETag = eTag; Check = Api.Found [] }

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

let private problemAt place severity message : Api.Problem =
  { Severity = severity
    Place = place
    Message = message }

let private warning place message = problemAt (Some place) Api.Severity.Warning message

let private error place message = problemAt (Some place) Api.Severity.Error message

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
      Expect.equal model.Editor (Some(Editor.Loading("my-post", Deploy.firstChain 1))) "The editor waits"
      let _, effect' = Shell.update (Shell.Session(Session.Started true)) model
      Expect.equal effect (Some(Shell.SignIn Session.Start)) "No call before the sign-in"

      Expect.equal
        effect'
        (Some(Shell.Send [ Shell.LoadQueue; Shell.LoadPost "my-post" ]))
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

    test "a typed letter marks the post unsaved and sends no save" {
      let model, effect = send (Editor.BodyChanged "Some text.\nMore") (opened draft)
      Expect.isNone effect "No save"
      Expect.equal (document' model).Fields.Body "Some text.\nMore" "The text"
      Expect.equal (status model) "Draft · unsaved changes" "The status"
    }

    test "the browser asks before it closes the page until a save answer comes" {
      let model = opened draft
      Expect.isFalse (Editor.isAtRisk model.Editor.Value) "Saved"

      let model, _ = send (Editor.BodyChanged "x") model
      Expect.isTrue (Editor.isAtRisk model.Editor.Value) "Unsaved"

      let model, _ = send Editor.SaveRequested model
      Expect.isTrue (Editor.isAtRisk model.Editor.Value) "Saving"

      let failed, _ = send (Editor.SaveAnswered("my-post", 1, Error Api.Unanswered)) model
      Expect.isTrue (Editor.isAtRisk failed.Editor.Value) "Save failed"

      let saved, _ = send (Editor.SaveAnswered("my-post", 1, Ok(savedAs "\"v2\""))) model
      Expect.isFalse (Editor.isAtRisk saved.Editor.Value) "Saved again"
    }

    test "typed letters stay unsaved until the author saves, and the save sends the last input" {
      let model, first = send (Editor.TitleChanged "A") (opened draft)
      let model, second = send (Editor.TitleChanged "AB") model
      Expect.isNone first "No save"
      Expect.isNone second "No save"
      Expect.equal (status model) "Draft · unsaved changes" "Still unsaved"

      let model, effect = send Editor.SaveRequested model

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.SavePost(request (Some "\"v1\"") "AB" "About it." "Some text.\n" 2) ]))
        "The save"

      Expect.equal (status model) "Draft · saving…" "The status"
    }

    test "a save answer shows saved and keeps the new ETag for the next save" {
      let model, _ = send (Editor.ExcerptChanged "New excerpt") (opened draft)
      let model, _ = send Editor.SaveRequested model
      let model, effect = send (Editor.SaveAnswered("my-post", 1, Ok(savedAs "\"v2\""))) model
      Expect.isNone effect "Nothing more to save"
      Expect.equal (status model) "Draft · saved" "Saved"

      let model, _ = send (Editor.BodyChanged "x") model
      let _, effect = send Editor.SaveRequested model
      Expect.equal (effect |> Option.map (function Shell.Send [ Shell.SavePost request ] -> request.ETag | _ -> None)) (Some(Some "\"v2\"")) "The new ETag"
    }

    test "the keys Command and S save at once" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, effect = send Editor.SaveRequested model
      Expect.equal effect (Some(Shell.Send [ Shell.SavePost(request (Some "\"v1\"") "My post" "About it." "x" 1) ])) "The save"
      Expect.equal (status model) "Draft · saving…" "The status"
    }

    test "the keys Command and S with no change save nothing" {
      let _, effect = send Editor.SaveRequested (opened draft)
      Expect.isNone effect "Nothing to save"
    }

    test "Command and S during a save with no new text sends no second save" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, _ = send Editor.SaveRequested model
      let model, effect = send Editor.SaveRequested model
      Expect.isNone effect "The save is in flight"
      Expect.equal (status model) "Draft · saving…" "Still saving"

      let model, effect = send (Editor.SaveAnswered("my-post", 1, Ok(savedAs "\"v2\""))) model
      Expect.isNone effect "The API has the text"
      Expect.equal (status model) "Draft · saved" "Saved"
    }

    test "text typed during a save is unsaved after the answer, and no save starts" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, _ = send Editor.SaveRequested model
      let model, _ = send (Editor.BodyChanged "xy") model
      let model, effect = send (Editor.SaveAnswered("my-post", 1, Ok(savedAs "\"v2\""))) model
      Expect.isNone effect "No save"
      Expect.equal (status model) "Draft · unsaved changes" "Unsaved"
      Expect.equal (document' model).Fields.Body "xy" "The text stays"
    }

    test "Command and S during a save saves again as soon as the answer comes" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, _ = send Editor.SaveRequested model
      let model, _ = send (Editor.BodyChanged "xy") model
      let model, none = send Editor.SaveRequested model
      Expect.isNone none "One save at a time"
      let _, effect = send (Editor.SaveAnswered("my-post", 1, Ok(savedAs "\"v2\""))) model
      Expect.equal effect (Some(Shell.Send [ Shell.SavePost(request (Some "\"v2\"") "My post" "About it." "xy" 2) ])) "The second save"
    }

    test "a failed save keeps the text, shows the failure, and saves again at the next Command and S" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, _ = send Editor.SaveRequested model
      let model, effect = send (Editor.SaveAnswered("my-post", 1, Error Api.Unanswered)) model
      Expect.equal (status model) "Draft · save failed" "The status"
      Expect.equal (document' model).Fields.Body "x" "The text stays"
      Expect.isNone effect "No retry"

      let model, effect = send Editor.SaveRequested model
      Expect.isSome effect "The second try"
      Expect.equal (status model) "Draft · saving…" "Saving again"
    }

    test "a save from an old tab fails with a stale answer, keeps the text, and tries nothing more" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, _ = send Editor.SaveRequested model
      let model, effect = send (Editor.SaveAnswered("my-post", 1, Error Api.Stale)) model
      Expect.isNone effect "No retry"
      Expect.equal (status model) "Draft · changed in another tab" "The status"
      Expect.equal (document' model).Fields.Body "x" "The text stays"

      let model, effect = send (Editor.BodyChanged "xy") model
      Expect.isNone effect "No save"
      let _, effect = send Editor.SaveRequested model
      Expect.isNone effect "No save"
    }

    test "a refused token on a save offers another account and keeps the text" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, _ = send Editor.SaveRequested model
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
      let model, _ = send (Editor.SaveAnswered("my-post", 1, Ok(savedAs "\"w1\""))) model
      Expect.equal (status model) "Published Feb 4, 2026 · changes not live · saved" "Saved"
      Expect.isTrue (document' model).PendingEdit "A pending edit"
    }

    test "an interaction error during a save keeps the text and shows the Sign in again banner" {
      let model, _ = send (Editor.BodyChanged "Words that I typed") (opened draft)
      let model, effect = send Editor.SaveRequested model
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

      let model, effect = send (Editor.SaveAnswered("my-post", 1, Ok(savedAs "\"v2\""))) model
      Expect.equal effect (Some(Shell.Navigate(Router.PushUrl "/posts", [ Shell.LoadPosts ]))) "Now the author leaves"
      Expect.equal model.Router.Route (Route.Posts PostFilter.All) "The post list"
      Expect.isNone model.Editor "The editor goes"
    }

    test "text typed during a save is saved before the author leaves" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, _ = send Editor.SaveRequested model
      let model, _ = send (Editor.BodyChanged "xy") model
      let model, effect = Shell.update (Shell.Router(Router.LinkFollowed "/posts")) model
      Expect.isNone effect "One save at a time"
      Expect.equal model.Router.Route (Route.Post "my-post") "The author stays"

      let model, effect = send (Editor.SaveAnswered("my-post", 1, Ok(savedAs "\"v2\""))) model

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.SavePost(request (Some "\"v2\"") "My post" "About it." "xy" 2) ]))
        "The newest text"

      Expect.equal model.Router.Route (Route.Post "my-post") "The author still stays"
      let model, effect = send (Editor.SaveAnswered("my-post", 2, Ok(savedAs "\"v3\""))) model
      Expect.equal effect (Some(Shell.Navigate(Router.PushUrl "/posts", [ Shell.LoadPosts ]))) "Now the author leaves"
      Expect.equal model.Router.Route (Route.Posts PostFilter.All) "The post list"
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
      Expect.equal model.Router.Route (Route.Posts PostFilter.All) "The second leave goes"
    }

    test "a leave after a session expiry saves the newest text after the waiting save" {
      let model, _ = send (Editor.BodyChanged "a") (opened draft)
      let model, effect = send Editor.SaveRequested model
      let call = effect |> Option.bind (function Shell.Send [ call ] -> Some call | _ -> None) |> Option.get
      let model, _ = Shell.update (Shell.Session(Session.InteractionRequired call)) model
      let model, _ = send (Editor.BodyChanged "ab") model
      let model, _ = Shell.update (Shell.Router(Router.LinkFollowed "/posts")) model
      Expect.equal model.Router.Route (Route.Post "my-post") "The author stays"

      let model, _ = Shell.update (Shell.Session Session.SignInAgainClicked) model
      let model, effect = Shell.update (Shell.Session Session.Renewed) model
      Expect.equal effect (Some(Shell.SignIn(Session.Retry [ call ]))) "The waiting save runs"

      let model, effect = send (Editor.SaveAnswered("my-post", 1, Ok(savedAs "\"v2\""))) model

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.SavePost(request (Some "\"v2\"") "My post" "About it." "ab" 2) ]))
        "The newest text"

      let model, _ = send (Editor.SaveAnswered("my-post", 2, Ok(savedAs "\"v3\""))) model
      Expect.equal model.Router.Route (Route.Posts PostFilter.All) "The author leaves"
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
      let after, _ = send (Editor.SaveAnswered("other", 1, Ok(savedAs "\"x\""))) model
      Expect.equal after model "No change"
    }

    test "a save answer with a problem shows the dot, and an answer with no problem clears it" {
      let problem = warning (Api.BodyLine 4) "The image has no alt text."
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, _ = send Editor.SaveRequested model
      Expect.isFalse (Editor.hasProblems (document' model)) "No answer yet"

      let model, _ = send (Editor.SaveAnswered("my-post", 1, Ok { savedAs "\"v2\"" with Check = Api.Found [ problem ] })) model
      Expect.isTrue (Editor.hasProblems (document' model)) "The dot"
      Expect.equal (document' model).Check (Some(Api.Found [ problem ])) "The problem"

      let model, _ = send (Editor.BodyChanged "xy") model
      let model, _ = send Editor.SaveRequested model
      let model, _ = send (Editor.SaveAnswered("my-post", 2, Ok(savedAs "\"v3\""))) model
      Expect.isFalse (Editor.hasProblems (document' model)) "No dot"
    }

    test "a warning shows the dot" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, _ = send Editor.SaveRequested model
      let answer = Ok { savedAs "\"v2\"" with Check = Api.Found [ warning (Api.Field "tags") "Add the tag 2026." ] }
      let model, _ = send (Editor.SaveAnswered("my-post", 1, answer)) model
      Expect.isTrue (Editor.hasProblems (document' model)) "The dot"
    }

    test "a late answer for an older version keeps the problems of the newer answer" {
      let older = Ok { savedAs "\"v2\"" with Check = Api.Found [ warning (Api.BodyLine 2) "Old." ] }
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, _ = send Editor.SaveRequested model
      let model, _ = send (Editor.SaveAnswered("my-post", 1, older)) model
      let model, _ = send (Editor.BodyChanged "xy") model
      let model, _ = send Editor.SaveRequested model
      let model, _ = send (Editor.SaveAnswered("my-post", 2, Ok(savedAs "\"v3\""))) model
      let model, _ = send (Editor.SaveAnswered("my-post", 1, older)) model

      Expect.equal (document' model).Check (Some(Api.Found [])) "The newer answer stays"
      Expect.equal (document' model).ETag (Some "\"v3\"") "The newer ETag"
    }

    test "a check that did not run shows no dot and keeps the save" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, _ = send Editor.SaveRequested model
      let model, _ = send (Editor.SaveAnswered("my-post", 1, Ok { savedAs "\"v2\"" with Check = Api.CheckFailed })) model
      Expect.equal (document' model).Check (Some Api.CheckFailed) "The check did not run"
      Expect.isFalse (Editor.hasProblems (document' model)) "No dot"
      Expect.equal (status model) "Draft · saved" "Saved"
    }

    test "a body problem moves the cursor to its line in the body" {
      let model, _ = send (Editor.DetailsMsg Details.Opened) (opened draft)

      let target = Problem.targetOf (warning (Api.BodyLine 4) "The image has no alt text.")
      Expect.equal target (Some(Problem.BodyLine 4)) "The line of the problem"

      let model, effect = send (Editor.ProblemChosen target.Value) model
      Expect.equal effect (Some(Shell.MoveToBodyLine 4)) "The move"
      Expect.isFalse (document' model).Details.Open "The sheet closes"
    }

    test "a field problem of the sheet focuses its control, and a field problem of the writing column closes the sheet" {
      let model, _ = send (Editor.DetailsMsg Details.Opened) (opened draft)
      let author = (Problem.targetOf (error (Api.Field "author") "The post has no author.")).Value

      let model, effect = send (Editor.ProblemChosen author) model
      Expect.equal effect (Some(Shell.Focus "details-author")) "The control"
      Expect.isTrue (document' model).Details.Open "The sheet stays"

      let title = (Problem.targetOf (error (Api.Field "title") "The post has no title.")).Value
      let model, effect = send (Editor.ProblemChosen title) model
      Expect.equal effect (Some(Shell.Focus "editor-title")) "The field"
      Expect.isFalse (document' model).Details.Open "The sheet closes"
    }

    test "Command and S after a check that did not run saves the same text again" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, _ = send Editor.SaveRequested model
      let model, _ = send (Editor.SaveAnswered("my-post", 1, Ok { savedAs "\"v2\"" with Check = Api.CheckFailed })) model
      let model, effect = send Editor.SaveRequested model

      Expect.equal effect (Some(Shell.Send [ Shell.SavePost(request (Some "\"v2\"") "My post" "About it." "x" 1) ])) "The save"
      Expect.equal (status model) "Draft · saving…" "The status"
    }

    test "a problem about the whole post, or about a field with no control, has no target" {
      Expect.isNone (Problem.targetOf { Severity = Api.Severity.Error; Place = None; Message = "A YAML error." }) "The whole post"
      Expect.isNone (Problem.targetOf (error (Api.Field "publishDate") "Bad date.")) "No control"
    }

    test "the primary action says Publish for a draft and Publish changes for a published post" {
      Expect.equal (Editor.primaryAction (document' (opened draft))) "Publish" "Draft"
      Expect.equal (Editor.primaryAction (document' (opened live))) "Publish changes" "Published"
    }

    test "Change on a draft with unsaved text saves, then sends the slug change with the new ETag" {
      let model, _ = send (Editor.TitleChanged "Hello world") (opened draft)
      let model, _ = send (Editor.DetailsMsg(Details.SlugTyped "hello-world")) model
      Expect.isTrue (Editor.canChangeSlug (document' model)) "Change is ready before the save"

      let model, effect = send Editor.SlugChangeRequested model

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.SavePost(request (Some "\"v1\"") "Hello world" "About it." "Some text.\n" 1) ]))
        "The save first"

      Expect.equal (document' model).Details.Slug Details.WaitingForSave "The slug change waits"

      let model, effect = send (Editor.SaveAnswered("my-post", 1, Ok(savedAs "\"v2\""))) model

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.ChangeSlug { Slug = "my-post"; ETag = "\"v2\""; NewSlug = "hello-world" } ]))
        "Then the slug change"

      Expect.equal (document' model).Details.Slug Details.Changing "Changing"
    }

    test "Change during a save waits for the save answer" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, _ = send Editor.SaveRequested model
      let model, _ = send (Editor.DetailsMsg(Details.SlugTyped "new-slug")) model
      let model, effect = send Editor.SlugChangeRequested model
      Expect.isNone effect "No second call"

      let _, effect = send (Editor.SaveAnswered("my-post", 1, Ok(savedAs "\"v2\""))) model

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.ChangeSlug { Slug = "my-post"; ETag = "\"v2\""; NewSlug = "new-slug" } ]))
        "The slug change"
    }

    test "a failed save before a slug change sends no slug change and shows the save error" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, _ = send (Editor.DetailsMsg(Details.SlugTyped "new-slug")) model
      let model, _ = send Editor.SlugChangeRequested model
      let model, effect = send (Editor.SaveAnswered("my-post", 1, Error(Api.Failed "The store is down."))) model
      Expect.isNone effect "No slug change"
      Expect.equal (document' model).Details.Slug Details.Idle "No slug change in progress"
      Expect.equal (Editor.failureText model.Editor.Value) "The store is down." "The save error"
      Expect.equal (document' model).Slug "my-post" "The old slug"
    }

    test "a failed slug change after the save keeps the draft saved with its old slug and shows the reason" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, _ = send (Editor.DetailsMsg(Details.SlugTyped "taken")) model
      let model, _ = send Editor.SlugChangeRequested model
      let model, _ = send (Editor.SaveAnswered("my-post", 1, Ok(savedAs "\"v2\""))) model
      let reason = "Another post already uses the slug 'taken'"
      let model, effect = send (Editor.SlugChangeAnswered("my-post", Error(Api.Failed reason))) model
      Expect.isNone effect "No call"
      Expect.equal (document' model).Save Editor.Saved "Saved"
      Expect.equal (document' model).Slug "my-post" "The old slug"
      Expect.equal (document' model).Details.Slug (Details.ChangeFailed reason) "The reason"
      Expect.equal model.Router.Route (Route.Post "my-post") "The route"
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
      Expect.equal model.Editor (Some(Editor.Loading("untitled", Deploy.firstChain 1))) "The editor loads"
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
