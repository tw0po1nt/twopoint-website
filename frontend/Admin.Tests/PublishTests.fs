module TwoPoint.Admin.Tests.PublishTests

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

let private draft = document Api.Draft false None (Some "\"v1\"")
let private live = document Api.Published false (Some "2026-02-04T13:00:00Z") None

let private opened (post: Api.PostDocument) =
  let model, _ = Shell.init "/posts/my-post"
  let model, _ = Shell.update (Shell.Session(Session.Started true)) model
  fst (Shell.update (Shell.Editor(Editor.Answered("my-post", Ok post))) model)

let private send msg (model: Shell.Model) = Shell.update (Shell.Editor msg) model

let private document' (model: Shell.Model) =
  match model.Editor with
  | Some(Editor.Loaded document) -> document
  | other -> failtestf "Expected a loaded editor, got %A" other

let private publishState (model: Shell.Model) = (document' model).Publish.State

let private problem severity message : Api.Problem =
  { Severity = severity
    Place = None
    Message = message }

let private checkRequest target : Editor.PublishRequest =
  { Slug = "my-post"
    ETag = "\"v1\""
    Target = target }

let private checkedWith problems = Ok({ Problems = problems }: Api.PublishCheck)

let private published state warnings : Api.PublishedPost =
  { PublishDate = "2026-03-01T09:30:45Z"
    State = state
    Warnings = warnings }

let private ready =
  let model, _ = send Editor.PublishClicked (opened draft)
  fst (send (Editor.PublishCheckAnswered("my-post", (Publish.Draft Publish.Now), checkedWith [])) model)

let private menuItem action (document: Editor.Document) =
  Editor.menu document |> List.find (fun item -> item.Action = action)

let private reasonOf action (model: Shell.Model) =
  match (menuItem action (document' model)).Availability with
  | Editor.Unavailable reason -> reason
  | Editor.Available -> ""

[<Tests>]
let tests =
  testList "Publish dialog" [
    test "Publish on a saved draft opens the dialog and asks for a check of now" {
      let model, effect = send Editor.PublishClicked (opened draft)
      Expect.equal (publishState model) (Publish.Checking (Publish.Draft Publish.Now)) "The dialog checks"
      Expect.equal effect (Some(Shell.Send [ Shell.CheckPublish(checkRequest (Api.Publish Api.PublishNow), (Publish.Draft Publish.Now)) ])) "The check call"
    }

    test "the check after the load asks the API again, because the load has no problems" {
      let model = opened draft
      Expect.isNone (document' model).Check "The load has no check"
      let _, effect = send Editor.PublishClicked model
      Expect.isSome effect "A fresh check"
    }

    test "Publish on an unsaved draft saves first and checks after the save" {
      let model, _ = send (Editor.BodyChanged "More text.\n") (opened draft)
      let model, effect = send Editor.PublishClicked model
      Expect.equal (publishState model) Publish.Saving "The dialog waits"

      match effect with
      | Some(Shell.Send [ Shell.SavePost request ]) ->
        let saved: Api.SavedPost = { ETag = "\"v2\""; Check = Api.Found [] }
        let model, effect = send (Editor.SaveAnswered("my-post", request.Version, Ok saved)) model
        Expect.equal (publishState model) (Publish.Checking (Publish.Draft Publish.Now)) "The check starts"

        Expect.equal
          effect
          (Some(Shell.Send [ Shell.CheckPublish({ checkRequest (Api.Publish Api.PublishNow) with ETag = "\"v2\"" }, (Publish.Draft Publish.Now)) ]))
          "The check has the new ETag"
      | other -> failtestf "Expected a save, got %A" other
    }

    test "a failed save before the publish shows the reason and starts no check" {
      let model, _ = send (Editor.BodyChanged "More text.\n") (opened draft)
      let model, effect = send Editor.PublishClicked model

      match effect with
      | Some(Shell.Send [ Shell.SavePost request ]) ->
        let model, effect = send (Editor.SaveAnswered("my-post", request.Version, Error(Api.Failed "No."))) model
        Expect.isNone effect "No check"
        Expect.stringContains (Publish.reason (document' model).Publish |> Option.get) "Couldn't save the post." "The reason"
      | other -> failtestf "Expected a save, got %A" other
    }

    test "a check with no problem enables the publish" {
      Expect.isTrue (Publish.canConfirm (document' ready).Publish) "Enabled"
    }

    test "a check with a warning enables the publish" {
      let model, _ = send Editor.PublishClicked (opened draft)
      let model, _ = send (Editor.PublishCheckAnswered("my-post", (Publish.Draft Publish.Now), checkedWith [ problem Api.Severity.Warning "Add the tag." ])) model
      Expect.isTrue (Publish.canConfirm (document' model).Publish) "A warning does not block"
    }

    test "a check with an error disables the publish and lists the problems" {
      let errors = [ problem Api.Severity.Warning "Add the tag."; problem Api.Severity.Error "Broken link." ]
      let model, _ = send Editor.PublishClicked (opened draft)
      let model, _ = send (Editor.PublishCheckAnswered("my-post", (Publish.Draft Publish.Now), checkedWith errors)) model
      Expect.isFalse (Publish.canConfirm (document' model).Publish) "Disabled"
      Expect.equal (Publish.problems (document' model).Publish) errors "The list"
      let model, effect = send Editor.PublishConfirmed model
      Expect.isNone effect "No publish call"
      Expect.equal (publishState model) (Publish.Checked((Publish.Draft Publish.Now), errors)) "The dialog stays"
    }

    test "the publish stays disabled until the check answers" {
      let model, _ = send Editor.PublishClicked (opened draft)
      Expect.isFalse (Publish.canConfirm (document' model).Publish) "Disabled while checking"
      let _, effect = send Editor.PublishConfirmed model
      Expect.isNone effect "No publish call"
    }

    test "a failed check shows the reason and keeps the publish disabled" {
      let model, _ = send Editor.PublishClicked (opened draft)
      let model, _ = send (Editor.PublishCheckAnswered("my-post", (Publish.Draft Publish.Now), Error(Api.Failed "The publish date is in the past"))) model
      Expect.isFalse (Publish.canConfirm (document' model).Publish) "Disabled"
      Expect.equal (Publish.reason (document' model).Publish) (Some "The publish date is in the past") "The reason"
    }

    test "a date choice asks for a check of that date, and an incomplete date asks for none" {
      let model, effect = send (Editor.PublishChoiceChanged((Publish.Draft(Publish.OnDate "")))) ready
      Expect.isNone effect "No check for an empty date"
      Expect.equal (publishState model) (Publish.Choosing((Publish.Draft(Publish.OnDate "")))) "Waiting for a date"
      Expect.isFalse (Publish.canConfirm (document' model).Publish) "Disabled"

      let model, effect = send (Editor.PublishChoiceChanged((Publish.Draft(Publish.OnDate "2099-12-31T13:00")))) model

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.CheckPublish(checkRequest ((Api.Publish(Api.PublishOn "2099-12-31T13:00:00Z"))), (Publish.Draft(Publish.OnDate "2099-12-31T13:00"))) ]))
        "The date goes to the API as a UTC time"

      Expect.equal (publishState model) (Publish.Checking((Publish.Draft(Publish.OnDate "2099-12-31T13:00")))) "Checking"
    }

    test "the answer of an earlier choice is ignored" {
      let model, _ = send (Editor.PublishChoiceChanged((Publish.Draft(Publish.OnDate "2099-12-31T13:00")))) ready
      let model, _ = send (Editor.PublishCheckAnswered("my-post", (Publish.Draft Publish.Now), checkedWith [])) model
      Expect.equal (publishState model) (Publish.Checking((Publish.Draft(Publish.OnDate "2099-12-31T13:00")))) "Still checking the date"
    }

    test "Publish after a check sends the publish call with the ETag and the date" {
      let model, effect = send Editor.PublishConfirmed ready
      Expect.equal (publishState model) (Publish.Sending (Publish.Draft Publish.Now)) "Sending"
      Expect.equal effect (Some(Shell.Send [ Shell.PublishPost(checkRequest (Api.Publish Api.PublishNow)) ])) "The publish call"
    }

    test "a published draft shows its state and closes the dialog" {
      let model, _ = send Editor.PublishConfirmed ready
      let model, effect = send (Editor.PublishAnswered("my-post", Ok(published Api.Published []))) model
      Expect.equal effect (Some(Shell.Send [ Shell.LoadPostDeploy "my-post" ])) "The read of the deploy state"
      Expect.equal (publishState model) Publish.Closed "The dialog closes"
      Expect.equal (Editor.statusText (document' model)) "Published Mar 1, 2026" "The status"
      Expect.isNone (document' model).ETag "No working blob"
      Expect.isEmpty (document' model).Publish.Warnings "No warning"
    }

    test "a draft published on a future date is upcoming" {
      let model, _ = send Editor.PublishConfirmed ready
      let model, _ = send (Editor.PublishAnswered("my-post", Ok(published Api.Upcoming []))) model
      Expect.equal (Editor.statusText (document' model)) "Upcoming Mar 1, 2026" "The status"
    }

    test "a publish with a failed step after the write shows the warning and keeps the post published" {
      let model, _ = send Editor.PublishConfirmed ready
      let model, _ = send (Editor.PublishAnswered("my-post", Ok(published Api.Published [ Api.SiteNotRebuilt ]))) model
      Expect.equal (document' model).State Api.Published "Published"
      Expect.equal (document' model).Publish.Warnings [ Api.SiteNotRebuilt ] "The warning"
      let model, _ = send Editor.PublishNoticeDismissed model
      Expect.isEmpty (document' model).Publish.Warnings "Dismissed"
    }

    test "a publish that could not delete the working blob keeps the ETag and the pending edit" {
      let model, _ = send Editor.PublishConfirmed ready
      let model, _ = send (Editor.PublishAnswered("my-post", Ok(published Api.Published [ Api.PendingEditKept ]))) model
      Expect.equal (document' model).ETag (Some "\"v1\"") "The working blob still has the ETag"
      Expect.isTrue (document' model).PendingEdit "A pending edit"
      Expect.equal (Editor.statusText (document' model)) "Published Mar 1, 2026 · changes not live · saved" "The status"
    }

    test "a publish that found newer text keeps a pending edit with no ETag, so the next save is refused" {
      let model, _ = send Editor.PublishConfirmed ready
      let model, _ = send (Editor.PublishAnswered("my-post", Ok(published Api.Published [ Api.NewerTextKept ]))) model
      Expect.isNone (document' model).ETag "No ETag"
      Expect.isTrue (document' model).PendingEdit "A pending edit"

      let model, _ = send (Editor.BodyChanged "More.\n") model
      let model, effect = send Editor.SaveRequested model

      match effect with
      | Some(Shell.Send [ Shell.SavePost request ]) ->
        Expect.isNone request.ETag "The save sends If-None-Match"
        let model, _ = send (Editor.SaveAnswered("my-post", request.Version, Error Api.Stale)) model
        Expect.equal (document' model).Save Editor.Stale "The save is refused as a change in another tab"
      | other -> failtestf "Expected a save, got %A" other
    }

    test "a refused publish shows the reason in the dialog and lets the author close it" {
      let model, _ = send Editor.PublishConfirmed ready
      let model, _ = send (Editor.PublishAnswered("my-post", Error(Api.Failed "The post has errors and is not published."))) model
      Expect.equal (document' model).State Api.Draft "Still a draft"
      Expect.equal (Publish.reason (document' model).Publish) (Some "The post has errors and is not published.") "The reason"
      let model, _ = send Editor.PublishClosed model
      Expect.equal (publishState model) Publish.Closed "Closed"
    }

    test "the dialog stays open while the publish is in flight" {
      let model, _ = send Editor.PublishConfirmed ready
      let model, _ = send Editor.PublishClosed model
      Expect.equal (publishState model) (Publish.Sending (Publish.Draft Publish.Now)) "Still sending"
    }

    test "the dialog is a layer over the editor, so Esc does not close the preview" {
      let model = opened draft
      let model, _ = send Editor.PreviewToggled model
      let model, _ = send Editor.PublishClicked model
      let closed, _ = Shell.update (Shell.Editor Editor.PreviewClosed) model
      Expect.equal closed model "Esc leaves the preview open under the dialog"
    }

    test "a published post has no publish dialog" {
      let model, effect = send Editor.PublishClicked (opened live)
      Expect.isNone effect "No call"
      Expect.equal (publishState model) Publish.Closed "Closed"
    }

    test "a post with a stale save cannot publish" {
      let model, _ = send (Editor.BodyChanged "x") (opened draft)
      let model, effect = send Editor.PublishClicked model
      let request =
        match effect with
        | Some(Shell.Send [ Shell.SavePost request ]) -> request
        | other -> failtestf "Expected a save, got %A" other

      let model, _ = send (Editor.SaveAnswered("my-post", request.Version, Error Api.Stale)) model
      Expect.equal (Editor.primaryAvailability (document' model)) (Editor.Unavailable "This post changed in another tab. Reload the page to get the latest version.") "The reason"
      let _, effect = send Editor.PublishClicked model
      Expect.isNone effect "No call"
    }
  ]

[<Tests>]
let menuTests =
  testList "Editor menu" [
    test "the primary action of a draft is available, and the primary action of a published post with no pending edit is not" {
      Expect.equal (Editor.primaryAvailability (document' (opened draft))) Editor.Available "Draft"
      Expect.equal (Editor.primaryAvailability (document' (opened live))) (Editor.Unavailable "The post has no changes to publish.") "Published"
    }

    test "the menu of a draft lists each action of the state, with the reason of each one that is not available" {
      let model = opened draft

      Expect.equal
        (Editor.menu (document' model) |> List.map (fun item -> item.Label))
        [ "Save"; "Compare with live"; "Discard changes"; "Change date"; "Unpublish"; "Revisions"; "Send to subscribers"; "Delete" ]
        "The actions"

      Expect.equal (reasonOf Editor.SaveNow model) "No unsaved changes." "Save"
      Expect.equal (reasonOf Editor.ChangesFromLive model) "Only for published posts." "Compare with live"
      Expect.equal (reasonOf Editor.DiscardChanges model) "Only for published posts." "Discard changes"
      Expect.equal (reasonOf Editor.ChangeDate model) "Only for upcoming posts." "Change date"
      Expect.equal (reasonOf Editor.Unpublish model) "Only for upcoming posts." "Unpublish"
      Expect.equal (reasonOf Editor.Revisions model) "Only for published posts." "Revisions"
      Expect.equal (reasonOf Editor.SendToSubscribers model) "Only for published posts." "Send to subscribers"
      Expect.equal (reasonOf Editor.Delete model) "" "Delete"
    }

    test "Save is available when the post has unsaved changes, and it sends the save message" {
      let model, _ = send (Editor.BodyChanged "More.\n") (opened draft)
      Expect.equal (menuItem Editor.SaveNow (document' model)).Availability Editor.Available "Available"
      Expect.equal (Editor.menuMsg Editor.SaveNow) (Some Editor.SaveRequested) "The message"
    }

    test "the menu of a published post says that only an upcoming post can change its date" {
      let model = opened live
      Expect.equal (reasonOf Editor.ChangeDate model) "Only for upcoming posts." "Change date"
      Expect.equal (reasonOf Editor.Unpublish model) "Only for upcoming posts." "Unpublish"
    }
  ]
