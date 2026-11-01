module TwoPoint.Admin.Tests.PendingEditTests

open TwoPoint.Admin

open Expecto

let private noDetails: Api.PostDetails =
  { Author = None
    Category = None
    Tags = []
    Series = None
    Image = None
    ShowOnHomepage = true }

let private pendingPost: Api.PostDocument =
  { Slug = "my-post"
    State = Api.Published
    PendingEdit = true
    PublishDate = Some "2026-02-04T13:00:00Z"
    ETag = Some "\"w1\""
    Title = "New title"
    Excerpt = "About it."
    Body = "First line.\nSecond line, edited.\nThird line.\n"
    Details = noDetails
    DeliveryStarted = None
    Text = ""
    Published =
      Some
        { Title = "Old title"
          Excerpt = "About it."
          Body = "First line.\nSecond line.\nThird line.\n"
          Details = noDetails } }

let private cleanPost = { pendingPost with PendingEdit = false; ETag = None; Title = "Old title"; Body = "First line.\nSecond line.\nThird line.\n" }

let private opened (post: Api.PostDocument) =
  let model, _ = Shell.init "/posts/my-post"
  let model, _ = Shell.update (Shell.Session(Session.Started true)) model
  fst (Shell.update (Shell.Editor(Editor.Answered("my-post", Ok post))) model)

let private send msg (model: Shell.Model) = Shell.update (Shell.Editor msg) model

let private document' (model: Shell.Model) =
  match model.Editor with
  | Some(Editor.Loaded document) -> document
  | other -> failtestf "Expected a loaded editor, got %A" other

let private request target : Editor.PublishRequest =
  { Slug = "my-post"
    ETag = "\"w1\""
    Target = target }

let private checkedWith problems = Ok({ Problems = problems }: Api.PublishCheck)

let private published warnings : Api.PublishedPost =
  { PublishDate = "2026-02-04T13:00:00Z"
    State = Api.Published
    Warnings = warnings }

let private ready =
  let model, _ = send Editor.PublishClicked (opened pendingPost)
  fst (send (Editor.PublishCheckAnswered("my-post", (Publish.Changes Publish.KeepDate), checkedWith [])) model)

let private reasonOf action (model: Shell.Model) =
  match (Editor.menu (document' model) |> List.find (fun item -> item.Action = action)).Availability with
  | Editor.Unavailable reason -> reason
  | Editor.Available -> ""

let private availabilityOf action (model: Shell.Model) =
  (Editor.menu (document' model) |> List.find (fun item -> item.Action = action)).Availability

[<Tests>]
let statusTests =
  testList "Pending edit status" [
    test "a published post with a pending edit says that the changes are not live" {
      Expect.equal (Editor.statusText (document' (opened pendingPost))) "Published Feb 4, 2026 · changes not live · saved" "The status"
    }

    test "the status follows the save of a pending edit" {
      let model, _ = send (Editor.BodyChanged "More.\n") (opened pendingPost)
      Expect.equal (Editor.statusText (document' model)) "Published Feb 4, 2026 · changes not live · unsaved changes" "Unsaved"
      let model, _ = send Editor.SaveRequested model
      Expect.equal (Editor.statusText (document' model)) "Published Feb 4, 2026 · changes not live · saving…" "Saving"
    }

    test "the primary action is Publish changes, and it is available when the post has a pending edit" {
      Expect.equal (Editor.primaryAction (document' (opened pendingPost))) "Publish changes" "The label"
      Expect.equal (Editor.primaryAvailability (document' (opened pendingPost))) Editor.Available "Available"
    }

    test "the primary action is not available when the post has no pending edit" {
      Expect.equal
        (Editor.primaryAvailability (document' (opened cleanPost)))
        (Editor.Unavailable "The post has no changes to publish.")
        "The reason"
    }

    test "the first edit of a published post makes the primary action available" {
      let model, _ = send (Editor.BodyChanged "More.\n") (opened cleanPost)
      Expect.equal (Editor.primaryAvailability (document' model)) Editor.Available "Available"
    }
  ]

[<Tests>]
let changesTests =
  testList "Changes from live" [
    test "the changes name each part that differs, with the lines that changed" {
      let parts = Editor.changesFromLive (document' (opened pendingPost))

      Expect.equal
        parts
        [ { Name = "Title"
            Entries = [ Diff.Shown(Diff.Removed "Old title"); Diff.Shown(Diff.Added "New title") ] }
          { Name = "Body"
            Entries =
              [ Diff.Shown(Diff.Same "First line.")
                Diff.Shown(Diff.Removed "Second line.")
                Diff.Shown(Diff.Added "Second line, edited.")
                Diff.Shown(Diff.Same "Third line.")
                Diff.Shown(Diff.Same "") ] } ]
        "The parts"
    }

    test "the changes follow the text on the screen, before a save" {
      let model, _ = send (Editor.ExcerptChanged "Another excerpt.") (opened cleanPost)

      Expect.equal
        (Editor.changesFromLive (document' model) |> List.map (fun part -> part.Name))
        [ "Excerpt" ]
        "Only the excerpt differs"
    }

    test "a change of the details is a part of the changes" {
      let model, _ = send (Editor.DetailsMsg(Details.AuthorChanged "Ada")) (opened cleanPost)

      match Editor.changesFromLive (document' model) with
      | [ { Name = "Details"; Entries = entries } ] ->
        Expect.contains entries (Diff.Shown(Diff.Removed "author: ")) "The old author"
        Expect.contains entries (Diff.Shown(Diff.Added "author: Ada")) "The new author"
      | other -> failtestf "Expected a Details part, got %A" other
    }

    test "a post with the published text has no changes" {
      Expect.isEmpty (Editor.changesFromLive (document' (opened cleanPost))) "No part"
    }

    test "Changes from live in the menu opens and closes the changes" {
      let model = opened pendingPost
      Expect.equal (availabilityOf Editor.ChangesFromLive model) Editor.Available "Available"
      Expect.equal (Editor.menuMsg Editor.ChangesFromLive) (Some Editor.ChangesOpened) "The message"
      let model, effect = send Editor.ChangesOpened model
      Expect.equal (document' model).Changes Editor.ChangesShown "Open"
      Expect.isNone effect "No call"
      let closed, _ = Shell.update (Shell.Editor Editor.PreviewClosed) model
      Expect.equal closed model "Esc closes only the top layer"
      let model, _ = send Editor.ChangesClosed model
      Expect.equal (document' model).Changes Editor.ChangesHidden "Closed"
    }

    test "a draft has no changes from live" {
      let draft = { pendingPost with State = Api.Draft; PublishDate = None; Published = None }
      Expect.equal (reasonOf Editor.ChangesFromLive (opened draft)) "A draft has no published text." "The reason"
      let model, _ = send Editor.ChangesOpened (opened draft)
      Expect.equal (document' model).Changes Editor.ChangesHidden "Stays closed"
    }
  ]

[<Tests>]
let publishTests =
  testList "Publish changes" [
    test "Publish changes on a saved pending edit asks for a check that keeps the update date" {
      let model, effect = send Editor.PublishClicked (opened pendingPost)
      Expect.equal (document' model).Publish.State (Publish.Checking (Publish.Changes Publish.KeepDate)) "The dialog checks"

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.CheckPublish(request (Api.PublishChanges Api.KeepUpdateDate), (Publish.Changes Publish.KeepDate)) ]))
        "The check call with the ETag of the working blob"
    }

    test "Publish changes on an unsaved edit saves first and checks after the save" {
      let model, _ = send (Editor.BodyChanged "More.\n") (opened pendingPost)
      let model, effect = send Editor.PublishClicked model
      Expect.equal (document' model).Publish.State Publish.Saving "The dialog waits"

      match effect with
      | Some(Shell.Send [ Shell.SavePost saveRequest ]) ->
        let saved: Api.SavedPost = { ETag = "\"w2\""; Check = Api.Found [] }
        let model, effect = send (Editor.SaveAnswered("my-post", saveRequest.Version, Ok saved)) model

        Expect.equal
          effect
          (Some(Shell.Send [ Shell.CheckPublish({ request (Api.PublishChanges Api.KeepUpdateDate) with ETag = "\"w2\"" }, (Publish.Changes Publish.KeepDate)) ]))
          "The check has the new ETag"

        Expect.equal (document' model).Publish.State (Publish.Checking (Publish.Changes Publish.KeepDate)) "The check starts"
      | other -> failtestf "Expected a save, got %A" other
    }

    test "a published post with no pending edit has no publish dialog" {
      let model, effect = send Editor.PublishClicked (opened cleanPost)
      Expect.isNone effect "No call"
      Expect.equal (document' model).Publish.State Publish.Closed "Closed"
    }

    test "a check with no problem enables the publish, and an error disables it" {
      Expect.isTrue (Publish.canConfirm (document' ready).Publish) "Enabled"

      let errors: Api.Problem list =
        [ { Severity = Api.Severity.Error
            Place = None
            Message = "Broken link." } ]

      let model, _ = send Editor.PublishClicked (opened pendingPost)
      let model, _ = send (Editor.PublishCheckAnswered("my-post", (Publish.Changes Publish.KeepDate), checkedWith errors)) model
      Expect.isFalse (Publish.canConfirm (document' model).Publish) "Disabled"
      Expect.equal (Publish.problems (document' model).Publish) errors "The list"
      let _, effect = send Editor.PublishConfirmed model
      Expect.isNone effect "No publish call"
    }

    test "an update date asks for a check with that date, and an incomplete date asks for none" {
      let model, effect = send (Editor.PublishChoiceChanged((Publish.Changes(Publish.UpdateOn "")))) ready
      Expect.isNone effect "No check for an empty date"
      Expect.equal (document' model).Publish.State (Publish.Choosing((Publish.Changes(Publish.UpdateOn "")))) "Waiting for a date"
      Expect.isFalse (Publish.canConfirm (document' model).Publish) "Disabled"

      let model, effect = send (Editor.PublishChoiceChanged((Publish.Changes(Publish.UpdateOn "2026-03-02T10:00")))) model

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.CheckPublish(request ((Api.PublishChanges(Api.SetUpdateDate "2026-03-02T10:00:00Z"))), (Publish.Changes(Publish.UpdateOn "2026-03-02T10:00"))) ]))
        "The date goes to the API as a UTC time"

      Expect.equal (document' model).Publish.State (Publish.Checking((Publish.Changes(Publish.UpdateOn "2026-03-02T10:00")))) "Checking"
    }

    test "the publish sends the update date with the ETag" {
      let model, _ = send (Editor.PublishChoiceChanged((Publish.Changes(Publish.UpdateOn "2026-03-02T10:00")))) ready
      let model, _ = send (Editor.PublishCheckAnswered("my-post", (Publish.Changes(Publish.UpdateOn "2026-03-02T10:00")), checkedWith [])) model
      let model, effect = send Editor.PublishConfirmed model
      Expect.equal (document' model).Publish.State (Publish.Sending((Publish.Changes(Publish.UpdateOn "2026-03-02T10:00")))) "Sending"
      Expect.equal effect (Some(Shell.Send [ Shell.PublishPost(request ((Api.PublishChanges(Api.SetUpdateDate "2026-03-02T10:00:00Z")))) ])) "The publish call"
    }

    test "published changes make the text live, and the editor has no pending edit" {
      let model, _ = send Editor.PublishConfirmed ready
      let model, effect = send (Editor.PublishAnswered("my-post", Ok(published []))) model
      Expect.equal effect (Some(Shell.Send [ Shell.LoadPostDeploy "my-post" ])) "The deploy read"
      let document = document' model
      Expect.equal document.Publish.State Publish.Closed "The dialog closes"
      Expect.isFalse document.PendingEdit "No pending edit"
      Expect.isNone document.ETag "No working blob"
      Expect.equal (Editor.statusText document) "Published Feb 4, 2026" "The status"
      Expect.isEmpty (Editor.changesFromLive document) "The text on the screen is the published text"
      Expect.equal (Editor.primaryAvailability document) (Editor.Unavailable "The post has no changes to publish.") "Nothing to publish"
    }

    test "published changes with a failed step after the write show the warning" {
      let model, _ = send Editor.PublishConfirmed ready
      let model, _ = send (Editor.PublishAnswered("my-post", Ok(published [ Api.SiteNotRebuilt ]))) model
      Expect.equal (document' model).Publish.Warnings [ Api.SiteNotRebuilt ] "The warning"
    }

    test "published changes that could not delete the working blob keep the pending edit" {
      let model, _ = send Editor.PublishConfirmed ready
      let model, _ = send (Editor.PublishAnswered("my-post", Ok(published [ Api.PendingEditKept ]))) model
      Expect.isTrue (document' model).PendingEdit "A pending edit"
      Expect.equal (document' model).ETag (Some "\"w1\"") "The ETag"
    }

    test "a refused publish of changes shows the reason in the dialog" {
      let model, _ = send Editor.PublishConfirmed ready
      let model, _ = send (Editor.PublishAnswered("my-post", Error(Api.Failed "The published text changed since you opened the post"))) model
      Expect.equal (Publish.reason (document' model).Publish) (Some "The published text changed since you opened the post") "The reason"
      Expect.isTrue (document' model).PendingEdit "The pending edit stays"
      Expect.equal (Publish.statusText (document' model).Publish) "The changes are not published." "The status"
    }
  ]

[<Tests>]
let discardTests =
  testList "Discard changes" [
    test "the menu offers the discard of a saved pending edit" {
      Expect.equal (availabilityOf Editor.DiscardChanges (opened pendingPost)) Editor.Available "Available"
      Expect.equal (Editor.menuMsg Editor.DiscardChanges) (Some Editor.DiscardRequested) "The message"
    }

    test "the menu gives the reason when the discard is not available" {
      Expect.equal (reasonOf Editor.DiscardChanges (opened cleanPost)) "The post has no pending edit." "No pending edit"

      let draft = { pendingPost with State = Api.Draft; Published = None }
      Expect.equal (reasonOf Editor.DiscardChanges (opened draft)) "A draft has no pending edit." "Draft"

      let model, _ = send (Editor.BodyChanged "More.\n") (opened pendingPost)
      Expect.equal (reasonOf Editor.DiscardChanges model) "Save the post first." "Unsaved text"
    }

    test "the discard asks first, and Keep closes the dialog with no call" {
      let model, effect = send Editor.DiscardRequested (opened pendingPost)
      Expect.equal (document' model).Discard Confirm.Confirming "The dialog"
      Expect.isNone effect "No call"
      let model, effect = send Editor.DiscardClosed model
      Expect.equal (document' model).Discard Confirm.Closed "Closed"
      Expect.isNone effect "No call"
    }

    test "a request for a discard that is not available does nothing" {
      let model, _ = send Editor.DiscardRequested (opened cleanPost)
      Expect.equal (document' model).Discard Confirm.Closed "No dialog"
    }

    test "the confirm sends the discard with the ETag of the working blob" {
      let model, _ = send Editor.DiscardRequested (opened pendingPost)
      let model, effect = send Editor.DiscardConfirmed model
      Expect.equal (document' model).Discard Confirm.Sending "Sending"
      Expect.equal effect (Some(Shell.Send [ Shell.DiscardPending { Slug = "my-post"; ETag = "\"w1\"" } ])) "The call"
    }

    test "the dialog stays open while the discard is in flight" {
      let model, _ = send Editor.DiscardRequested (opened pendingPost)
      let model, _ = send Editor.DiscardConfirmed model
      let model, _ = send Editor.DiscardClosed model
      Expect.equal (document' model).Discard Confirm.Sending "Still sending"
    }

    test "a discard that succeeded loads the post again, so the editor shows the published text" {
      let model, _ = send Editor.DiscardRequested (opened pendingPost)
      let model, _ = send Editor.DiscardConfirmed model
      let model, effect = send (Editor.DiscardAnswered("my-post", Ok())) model
      Expect.equal model.Editor (Some(Editor.Loading("my-post", { Page = 1; Number = 1 }))) "Loading, with a new chain of deploy reads"
      Expect.equal effect (Some(Shell.Send [ Shell.LoadPost "my-post" ])) "The load call"

      let model, _ = send (Editor.Answered("my-post", Ok cleanPost)) model
      Expect.equal (document' model).Fields.Title "Old title" "The published title"
      Expect.isFalse (document' model).PendingEdit "No pending edit"
      Expect.equal (Editor.statusText (document' model)) "Published Feb 4, 2026" "The status"
    }

    test "a refused discard shows the reason and lets the author try again" {
      let model, _ = send Editor.DiscardRequested (opened pendingPost)
      let model, _ = send Editor.DiscardConfirmed model
      let model, _ = send (Editor.DiscardAnswered("my-post", Error(Api.Failed "The post changed since you opened it"))) model
      Expect.equal (document' model).Discard (Confirm.Failed "The post changed since you opened it") "The reason"
      Expect.isTrue (document' model).PendingEdit "The pending edit stays"
      let _, effect = send Editor.DiscardConfirmed model
      Expect.isSome effect "A second call"
    }

    test "a discard that the API refuses for the account signs the author out" {
      let model, _ = send Editor.DiscardRequested (opened pendingPost)
      let model, _ = send Editor.DiscardConfirmed model
      let next, _ = send (Editor.DiscardAnswered("my-post", Error Api.Refused)) model
      Expect.notEqual next.Session model.Session "The session changes"
    }

    test "the dialog is a layer over the editor, so Esc does not close the preview" {
      let model, _ = send Editor.PreviewToggled (opened pendingPost)
      let model, _ = send Editor.DiscardRequested model
      let closed, _ = Shell.update (Shell.Editor Editor.PreviewClosed) model
      Expect.equal closed model "Esc leaves the preview open under the dialog"
    }
  ]
