module TwoPoint.Admin.Tests.MoveTests

open TwoPoint.Admin

open Expecto

let private noDetails: Api.PostDetails =
  { Author = None
    Category = None
    Tags = []
    Series = None
    Image = None
    ShowOnHomepage = true }

let private post state pendingEdit publishDate eTag : Api.PostDocument =
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
    Published =
      Some
        { Title = "My post"
          Excerpt = "About it."
          Body = "Some text.\n"
          Details = noDetails } }

let private upcoming = post Api.Upcoming false (Some "2099-12-31T00:00:00Z") None
let private upcomingPending = post Api.Upcoming true (Some "2099-12-31T00:00:00Z") (Some "\"w1\"")
let private live = post Api.Published false (Some "2026-02-04T13:00:00Z") None

let private opened (post: Api.PostDocument) =
  let model, _ = Shell.init "/posts/my-post"
  let model, _ = Shell.update (Shell.Session(Session.Started true)) model
  fst (Shell.update (Shell.Editor(Editor.Answered("my-post", Ok post))) model)

let private send msg (model: Shell.Model) = Shell.update (Shell.Editor msg) model

let private document' (model: Shell.Model) =
  match model.Editor with
  | Some(Editor.Loaded document) -> document
  | other -> failtestf "Expected a loaded editor, got %A" other

let private availabilityOf action (model: Shell.Model) =
  (Editor.menu (document' model) |> List.find (fun item -> item.Action = action)).Availability

let private reasonOf action (model: Shell.Model) =
  match availabilityOf action model with
  | Editor.Unavailable reason -> reason
  | Editor.Available -> ""

let private change state publishDate eTag warnings : Api.UpcomingChange =
  { PublishDate = publishDate
    State = state
    ETag = eTag
    Warnings = warnings }

let private requested post = fst (send Editor.UnpublishRequested (opened post))

let private checkedAs outcome model =
  fst (send (Editor.RemovalCheckAnswered("my-post", Editor.UnpublishDialog, Ok outcome)) model)

/// The dialog is open, and the check found no link to the post.
let private ready post = checkedAs Api.PlainRemoval (requested post)

let private edit slug title: Api.RemovalEdit = { Slug = slug; Title = title; ETag = $"\"{slug}-w\"" }

let private problem message: Api.Problem =
  { Severity = Api.Severity.Error
    Place = None
    Message = message }

[<Tests>]
let menuTests =
  testList "Move menu" [
    test "the menu offers both actions for a saved upcoming post, and each action sends its message" {
      Expect.equal (availabilityOf Editor.Unpublish (opened upcoming)) Editor.Available "Unpublish"
      Expect.equal (availabilityOf Editor.ChangeDate (opened upcomingPending)) Editor.Available "Change date"
      Expect.equal (Editor.menuMsg Editor.Unpublish) (Some Editor.UnpublishRequested) "Unpublish message"
      Expect.equal (Editor.menuMsg Editor.ChangeDate) (Some Editor.ChangeDateRequested) "Change the date message"
    }

    test "the menu gives the reason when the post is not upcoming" {
      Expect.equal (reasonOf Editor.Unpublish (opened live)) "Only for upcoming posts." "Published"
      Expect.equal (reasonOf Editor.ChangeDate (opened live)) "Only for upcoming posts." "Published"

      let draft = post Api.Draft false None (Some "\"v1\"")
      Expect.equal (reasonOf Editor.Unpublish (opened draft)) "Only for upcoming posts." "Draft"
    }

    test "the menu gives the reason when the text is not saved" {
      let model, _ = send (Editor.BodyChanged "More.\n") (opened upcoming)
      Expect.equal (reasonOf Editor.Unpublish model) "Save the post first." "Unpublish"
      Expect.equal (reasonOf Editor.ChangeDate model) "Save the post first." "Change date"
    }

    test "a pending edit with no ETag needs a reload" {
      let model = opened { upcomingPending with ETag = None }
      Expect.equal (reasonOf Editor.Unpublish model) "Reload the page, then try again." "Unpublish"
    }
  ]

[<Tests>]
let unpublishTests =
  testList "Unpublish" [
    test "the dialog runs the check one time when it opens, and Keep closes the dialog with no call" {
      let model, effect = send Editor.UnpublishRequested (opened upcoming)
      Expect.equal (document' model).Unpublish Confirm.Confirming "The dialog"
      Expect.equal (document' model).UnpublishCheck RemovalCheck.Checking "The check runs"
      Expect.equal effect (Some(Shell.Send [ Shell.CheckRemoval("my-post", Editor.UnpublishDialog) ])) "The check"
      let model, effect = send Editor.UnpublishClosed model
      Expect.equal (document' model).Unpublish Confirm.Closed "Closed"
      Expect.equal (document' model).UnpublishCheck RemovalCheck.NotChecked "No check"
      Expect.isNone effect "No call"
    }

    test "a request for an unpublish that is not available does nothing" {
      let model, effect = send Editor.UnpublishRequested (opened live)
      Expect.equal (document' model).Unpublish Confirm.Closed "No dialog"
      Expect.isNone effect "No check"
    }

    test "the Unpublish button is disabled while the check runs" {
      let model = requested upcoming
      Expect.isFalse (Editor.canUnpublish (document' model)) "Disabled"
      Expect.equal (Editor.unpublishButtonText (document' model)) "Unpublish" "The button"
      let _, effect = send Editor.UnpublishConfirmed model
      Expect.isNone effect "No unpublish during the check"
    }

    test "the confirm sends the unpublish with the ETag of the pending edit and no edits" {
      let model = ready upcomingPending
      Expect.isTrue (Editor.canUnpublish (document' model)) "Enabled"
      let model, effect = send Editor.UnpublishConfirmed model
      Expect.equal (document' model).Unpublish Confirm.Sending "Sending"
      Expect.isFalse (Editor.canUnpublish (document' model)) "Disabled while it runs"

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.UnpublishPost { Slug = "my-post"; ETag = Some "\"w1\""; Edits = [] } ]))
        "The call"
    }

    test "the confirm for a post with no pending edit sends no ETag" {
      let _, effect = send Editor.UnpublishConfirmed (ready upcoming)
      Expect.equal effect (Some(Shell.Send [ Shell.UnpublishPost { Slug = "my-post"; ETag = None; Edits = [] } ])) "The call"
    }

    test "a publish and remove lists the posts, gives the number of edits on the button, and sends each edit with its ETag" {
      let edits = [ edit "a-post" "A post"; edit "c-post" "C post" ]
      let model = checkedAs (Api.PublishAndRemove edits) (requested upcoming)
      Expect.equal (RemovalCheck.edits (document' model).UnpublishCheck) edits "The posts"
      Expect.equal (Editor.unpublishButtonText (document' model)) "Publish 2 edits and unpublish" "The button"
      Expect.isTrue (Editor.canUnpublish (document' model)) "Enabled"
      let one = checkedAs (Api.PublishAndRemove [ edit "a-post" "A post" ]) (requested upcoming)
      Expect.equal (Editor.unpublishButtonText (document' one)) "Publish 1 edit and unpublish" "One edit"

      let _, effect = send Editor.UnpublishConfirmed model
      Expect.equal effect (Some(Shell.Send [ Shell.UnpublishPost { Slug = "my-post"; ETag = None; Edits = edits } ])) "The call"
    }

    test "a blocked check shows the problems and disables the unpublish" {
      let problems = [ problem "The post 'c-post' links to the post and has no pending edit." ]
      let model = checkedAs (Api.Blocked problems) (requested upcoming)
      Expect.isFalse (Editor.canUnpublish (document' model)) "Disabled"
      Expect.equal (RemovalCheck.problems (document' model).UnpublishCheck) problems "The problems"
      Expect.equal (Editor.unpublishButtonText (document' model)) "Unpublish" "The button"
      let _, effect = send Editor.UnpublishConfirmed model
      Expect.isNone effect "No call"
    }

    test "a check that fails disables the unpublish and gives the reason" {
      let model =
        fst (send (Editor.RemovalCheckAnswered("my-post", Editor.UnpublishDialog, Error Api.Unanswered)) (requested upcoming))

      Expect.isFalse (Editor.canUnpublish (document' model)) "Disabled"
      Expect.equal (document' model).UnpublishCheck (RemovalCheck.Failed "The server didn't respond. Check your connection and try again.") "The reason"
    }

    test "a check answer after the dialog closes, or for the delete dialog, changes nothing" {
      let closed, _ = send Editor.UnpublishClosed (requested upcoming)
      Expect.equal (document' (checkedAs Api.PlainRemoval closed)).UnpublishCheck RemovalCheck.NotChecked "Closed"
      let model = requested upcoming

      let other =
        fst (send (Editor.RemovalCheckAnswered("my-post", Editor.DeleteDialog, Ok Api.PlainRemoval)) model)

      Expect.equal (document' other).UnpublishCheck RemovalCheck.Checking "The unpublish check still runs"
    }

    test "a check that the API refuses for the account signs the author out" {
      let model = requested upcoming
      let next, _ = send (Editor.RemovalCheckAnswered("my-post", Editor.UnpublishDialog, Error Api.Refused)) model
      Expect.notEqual next.Session model.Session "The session changes"
    }

    test "the dialog stays open while the unpublish is in flight" {
      let model, _ = send Editor.UnpublishConfirmed (ready upcoming)
      let model, _ = send Editor.UnpublishClosed model
      Expect.equal (document' model).Unpublish Confirm.Sending "Still sending"
    }

    test "an unpublish that succeeded shows a draft with the new ETag, and restarts the deploy reads" {
      let model, _ = send Editor.UnpublishConfirmed (ready upcomingPending)
      let before = (document' model).Deploy
      let answer = Ok(change Api.Draft None (Some "\"w2\"") [])
      let model, effect = send (Editor.UnpublishAnswered("my-post", answer)) model
      let document = document' model

      Expect.equal document.State Api.Draft "A draft"
      Expect.equal document.PublishDate None "No publish date"
      Expect.equal document.ETag (Some "\"w2\"") "The ETag of the draft"
      Expect.isFalse document.PendingEdit "A draft has no pending edit"
      Expect.isNone document.Published "No published text"
      Expect.equal document.Unpublish Confirm.Closed "Closed"
      Expect.equal document.UnpublishCheck RemovalCheck.NotChecked "No check"
      Expect.equal document.Fields.Body "Some text.\n" "The text stays"
      Expect.equal document.Deploy.Chain.Number (before.Chain.Number + 1) "A new chain of deploy reads"
      Expect.equal effect (Some(Shell.Send [ Shell.LoadPostDeploy "my-post" ])) "The deploy read"
      Expect.equal (Editor.statusText document) "Draft · saved" "The status"
      Expect.equal (availabilityOf Editor.Unpublish model) (Editor.Unavailable "Only for upcoming posts.") "Not upcoming"
    }

    test "the draft can publish with the ETag that the unpublish gave" {
      let model, _ = send Editor.UnpublishConfirmed (ready upcoming)
      let model, _ = send (Editor.UnpublishAnswered("my-post", Ok(change Api.Draft None (Some "\"w1\"") []))) model
      let _, effect = send Editor.PublishClicked model

      match effect with
      | Some(Shell.Send [ Shell.CheckPublish(request, _) ]) -> Expect.equal request.ETag "\"w1\"" "The ETag"
      | other -> failtestf "Expected a check, got %A" other
    }

    test "the warnings of an unpublish show until the author dismisses them" {
      let model, _ = send Editor.UnpublishConfirmed (ready upcoming)
      let model, _ = send (Editor.UnpublishAnswered("my-post", Ok(change Api.Draft None (Some "\"w1\"") [ Api.PostRowKept ]))) model
      Expect.equal (document' model).ChangeWarnings [ Api.PostRowKept ] "The warning"
      let model, _ = send Editor.ChangeNoticeDismissed model
      Expect.isEmpty (document' model).ChangeWarnings "Dismissed"
    }

    test "a refused unpublish shows the reason, runs the check again, and lets the author try again" {
      let model, _ = send Editor.UnpublishConfirmed (ready upcoming)
      let model, effect = send (Editor.UnpublishAnswered("my-post", Error(Api.Failed "The post 'my-post' is not upcoming. Its publish date is in the past"))) model
      Expect.equal (Confirm.reason (document' model).Unpublish) (Some "The post 'my-post' is not upcoming. Its publish date is in the past") "The reason"
      Expect.equal (document' model).State Api.Upcoming "Still upcoming"
      Expect.equal effect (Some(Shell.Send [ Shell.CheckRemoval("my-post", Editor.UnpublishDialog) ])) "The check runs again"
      let _, effect = send Editor.UnpublishConfirmed model
      Expect.isNone effect "No call during the check"
      let _, effect = send Editor.UnpublishConfirmed (checkedAs Api.PlainRemoval model)
      Expect.isSome effect "A second call"
    }

    test "an unpublish that the API refuses for the account signs the author out" {
      let model, _ = send Editor.UnpublishConfirmed (ready upcoming)
      let next, _ = send (Editor.UnpublishAnswered("my-post", Error Api.Refused)) model
      Expect.notEqual next.Session model.Session "The session changes"
    }

    test "the dialog is a layer over the editor, so Esc does not close the preview" {
      let model, _ = send Editor.PreviewToggled (opened upcoming)
      let model, _ = send Editor.UnpublishRequested model
      let closed, _ = Shell.update (Shell.Editor Editor.PreviewClosed) model
      Expect.equal closed model "Esc leaves the preview open under the dialog"
    }
  ]

[<Tests>]
let changeDateTests =
  testList "Change date" [
    test "the dialog starts with the current date in the field" {
      let model, effect = send Editor.ChangeDateRequested (opened upcoming)
      Expect.equal (document' model).ChangeDate (Typed.Typing "2099-12-31T00:00") "The field"
      Expect.isNone effect "No call"
    }

    test "a request for a date change that is not available does nothing" {
      let model, _ = send Editor.ChangeDateRequested (opened live)
      Expect.equal (document' model).ChangeDate Typed.Closed "No dialog"
    }

    test "the confirm needs a complete date" {
      let model, _ = send Editor.ChangeDateRequested (opened upcoming)
      let model, _ = send (Editor.ChangeDateTyped "2099-12") model
      Expect.isFalse (ChangeDate.canConfirm (document' model).ChangeDate) "Not complete"
      let _, effect = send Editor.ChangeDateConfirmed model
      Expect.isNone effect "No call"
    }

    test "the confirm sends the date in UTC with the ETag of the pending edit" {
      let model, _ = send Editor.ChangeDateRequested (opened upcomingPending)
      let model, _ = send (Editor.ChangeDateTyped "2099-06-01T08:30") model
      let model, effect = send Editor.ChangeDateConfirmed model
      Expect.equal (document' model).ChangeDate (Typed.Sending "2099-06-01T08:30") "Sending"

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.ChangePostDate { Slug = "my-post"; ETag = Some "\"w1\""; PublishDate = "2099-06-01T08:30:00Z" } ]))
        "The call"
    }

    test "a date change that succeeded shows the new date and restarts the deploy reads" {
      let model, _ = send Editor.ChangeDateRequested (opened upcoming)
      let model, _ = send (Editor.ChangeDateTyped "2099-06-01T08:30") model
      let model, _ = send Editor.ChangeDateConfirmed model
      let before = (document' model).Deploy
      let answer = Ok(change Api.Upcoming (Some "2099-06-01T08:30:00Z") None [])
      let model, effect = send (Editor.ChangeDateAnswered("my-post", answer)) model
      let document = document' model

      Expect.equal document.PublishDate (Some "2099-06-01T08:30:00Z") "The new date"
      Expect.equal document.State Api.Upcoming "Upcoming"
      Expect.equal document.ChangeDate Typed.Closed "Closed"
      Expect.isNone document.ETag "No pending edit, so no ETag"
      Expect.equal document.Deploy.Chain.Number (before.Chain.Number + 1) "A new chain of deploy reads"
      Expect.equal effect (Some(Shell.Send [ Shell.LoadPostDeploy "my-post" ])) "The deploy read"
      Expect.equal (Editor.statusText document) "Upcoming Jun 1, 2099" "The status"
    }

    test "a new date in the past shows a published post, and the menu gives the reason for the date" {
      let model, _ = send Editor.ChangeDateRequested (opened upcoming)
      let model, _ = send (Editor.ChangeDateTyped "2026-01-01T08:30") model
      let model, _ = send Editor.ChangeDateConfirmed model
      let answer = Ok(change Api.Published (Some "2026-01-01T08:30:00Z") None [])
      let model, _ = send (Editor.ChangeDateAnswered("my-post", answer)) model

      Expect.equal (document' model).State Api.Published "Published"
      Expect.equal (reasonOf Editor.ChangeDate model) "Only for upcoming posts." "Not upcoming"
    }

    test "a date change keeps the pending edit and takes its new ETag" {
      let model, _ = send Editor.ChangeDateRequested (opened upcomingPending)
      let model, _ = send Editor.ChangeDateConfirmed model
      let answer = Ok(change Api.Upcoming (Some "2099-12-31T00:00:00Z") (Some "\"w2\"") [])
      let model, _ = send (Editor.ChangeDateAnswered("my-post", answer)) model

      Expect.isTrue (document' model).PendingEdit "The pending edit stays"
      Expect.equal (document' model).ETag (Some "\"w2\"") "The new ETag"
    }

    test "a refused date change shows the reason in the dialog" {
      let model, _ = send Editor.ChangeDateRequested (opened upcoming)
      let model, _ = send Editor.ChangeDateConfirmed model
      let model, _ = send (Editor.ChangeDateAnswered("my-post", Error(Api.Failed "The post changed since you opened it"))) model
      Expect.equal (Typed.reason (document' model).ChangeDate) (Some "The post changed since you opened it") "The reason"
      Expect.isTrue (ChangeDate.canConfirm (document' model).ChangeDate) "The author can try again"
    }

    test "a date change that the API refuses for the account signs the author out" {
      let model, _ = send Editor.ChangeDateRequested (opened upcoming)
      let model, _ = send Editor.ChangeDateConfirmed model
      let next, _ = send (Editor.ChangeDateAnswered("my-post", Error Api.Refused)) model
      Expect.notEqual next.Session model.Session "The session changes"
    }

    test "the dialog stays open while the date change is in flight" {
      let model, _ = send Editor.ChangeDateRequested (opened upcoming)
      let model, _ = send Editor.ChangeDateConfirmed model
      let model, _ = send Editor.ChangeDateClosed model
      Expect.equal (document' model).ChangeDate (Typed.Sending "2099-12-31T00:00") "Still sending"
    }
  ]

[<Tests>]
let warningTests =
  testList "Change warnings" [
    test "the text of a kept row says that readers can comment until the row is deleted" {
      Expect.equal
        (Publish.changeWarningText Api.PostRowKept)
        "Comments are still open on this post (its post-table row wasn't deleted)."
        "The text"
    }
  ]
