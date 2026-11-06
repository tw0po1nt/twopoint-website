module TwoPoint.Admin.Tests.RevisionsTests

open TwoPoint.Admin

open Expecto

let private noDetails: Api.PostDetails =
  { Author = None
    Category = None
    Tags = []
    Series = None
    Image = None
    ShowOnHomepage = true }

let private livePost: Api.PostDocument =
  { Slug = "my-post"
    State = Api.Published
    PendingEdit = false
    PublishDate = Some "2026-02-04T13:00:00Z"
    ETag = None
    Title = "Live title"
    Excerpt = "About it."
    Body = "Live body.\n"
    Details = noDetails
    DeliveryStarted = None
    Text = ""
    Published =
      Some
        { Title = "Live title"
          Excerpt = "About it."
          Body = "Live body.\n"
          Details = noDetails } }

let private pendingPost =
  { livePost with
      PendingEdit = true
      ETag = Some "\"w1\""
      Body = "Edited body.\n" }

let private restoredPost =
  { livePost with
      PendingEdit = true
      ETag = Some "\"w2\""
      Title = "First title"
      Body = "First body.\n" }

let private draft =
  { livePost with
      State = Api.Draft
      PublishDate = None
      ETag = Some "\"w1\""
      Published = None }

let private newer: Api.Revision =
  { Name = "2026-02-04T13:00:00Z"
    PublishedAt = "2026-02-04T13:00:00Z"
    Title = Some "Live title" }

let private older: Api.Revision =
  { Name = "2026-01-01T00:00:00.0000000Z"
    PublishedAt = "2026-01-01T00:00:00Z"
    Title = Some "First title" }

let private opened (post: Api.PostDocument) =
  let model, _ = Shell.init "/posts/my-post"
  let model, _ = Shell.update (Shell.Session(Session.Started true)) model
  fst (Shell.update (Shell.Editor(Editor.Answered("my-post", Ok post))) model)

let private send msg (model: Shell.Model) = Shell.update (Shell.Editor msg) model

let private document' (model: Shell.Model) =
  match model.Editor with
  | Some(Editor.Loaded document) -> document
  | other -> failtestf "Expected a loaded editor, got %A" other

let private reasonOf action (model: Shell.Model) =
  match (Editor.menu (document' model) |> List.find (fun item -> item.Action = action)).Availability with
  | Editor.Unavailable reason -> reason
  | Editor.Available -> ""

let private availabilityOf action (model: Shell.Model) =
  (Editor.menu (document' model) |> List.find (fun item -> item.Action = action)).Availability

let private listed (post: Api.PostDocument) =
  let model, _ = send Editor.RevisionsRequested (opened post)
  fst (send (Editor.RevisionsAnswered("my-post", Ok [ newer; older ])) model)

[<Tests>]
let tests =
  testList "Revisions" [
    test "the menu offers the revisions of a published post" {
      Expect.equal (availabilityOf Editor.Revisions (opened livePost)) Editor.Available "Available"
      Expect.equal (availabilityOf Editor.Revisions (opened pendingPost)) Editor.Available "Available with a pending edit"
      Expect.equal (Editor.menuMsg Editor.Revisions) (Some Editor.RevisionsRequested) "The message"
    }

    test "the menu gives the reason when the revisions are not available" {
      Expect.equal (reasonOf Editor.Revisions (opened draft)) "Only for published posts." "Draft"
      let model, _ = send (Editor.BodyChanged "More.\n") (opened pendingPost)
      Expect.equal (reasonOf Editor.Revisions model) "Save the post first." "Unsaved text"
    }

    test "a request for the revisions opens the dialog and loads the list" {
      let model, effect = send Editor.RevisionsRequested (opened livePost)
      Expect.equal (document' model).Revisions Revisions.Loading "Loading"
      Expect.equal effect (Some(Shell.Send [ Shell.LoadRevisions "my-post" ])) "The list call"
    }

    test "a request for revisions that are not available does nothing" {
      let model, effect = send Editor.RevisionsRequested (opened draft)
      Expect.equal (document' model).Revisions Revisions.Closed "No dialog"
      Expect.isNone effect "No call"
    }

    test "the dialog lists each revision that the API sends" {
      let model = listed livePost
      Expect.equal (document' model).Revisions (Revisions.Listed [ newer; older ]) "The list"
      Expect.equal (Revisions.revisions (document' model).Revisions) [ newer; older ] "The revisions"
    }

    test "a list that did not load shows the reason" {
      let model, _ = send Editor.RevisionsRequested (opened livePost)
      let model, _ = send (Editor.RevisionsAnswered("my-post", Error Api.Unanswered)) model
      Expect.equal (Revisions.reason (document' model).Revisions) (Some "The server didn't respond. Check your connection and try again.") "The reason"
    }

    test "a list for another post changes nothing" {
      let model, _ = send Editor.RevisionsRequested (opened livePost)
      let next, _ = send (Editor.RevisionsAnswered("other-post", Ok [ newer ])) model
      Expect.equal (document' next).Revisions Revisions.Loading "Still loading"
    }

    test "a restore of a post with no pending edit sends the revision with no ETag" {
      let model, effect = send (Editor.RestoreChosen older.Name) (listed livePost)
      Expect.equal (document' model).Revisions (Revisions.Restoring([ newer; older ], older.Name)) "Restoring"

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.RestoreRevision { Slug = "my-post"; ETag = None; Revision = older.Name } ]))
        "The restore call"
    }

    test "a restore over a pending edit sends the ETag of the working blob" {
      let _, effect = send (Editor.RestoreChosen older.Name) (listed pendingPost)

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.RestoreRevision { Slug = "my-post"; ETag = Some "\"w1\""; Revision = older.Name } ]))
        "The restore call"
    }

    test "a revision that is not in the list is not restored" {
      let model = listed livePost
      let next, effect = send (Editor.RestoreChosen "2025-05-05T00:00:00Z") model
      Expect.equal next model "No change"
      Expect.isNone effect "No call"
    }

    test "the dialog stays open while the restore is in flight" {
      let model, _ = send (Editor.RestoreChosen older.Name) (listed livePost)
      let model, _ = send Editor.RevisionsClosed model
      Expect.isTrue (Revisions.isRestoring (document' model).Revisions) "Still restoring"
    }

    test "a restore that succeeded loads the post again, so the editor shows the revision as a pending edit" {
      let model, _ = send (Editor.RestoreChosen older.Name) (listed livePost)
      let model, effect = send (Editor.RestoreAnswered("my-post", Ok { ETag = "\"w2\"" })) model
      Expect.equal model.Editor (Some(Editor.Loading("my-post", { Page = 1; Number = 1 }))) "Loading, with a new chain of deploy reads"
      Expect.equal effect (Some(Shell.Send [ Shell.LoadPost "my-post" ])) "The load call"

      let model, _ = send (Editor.Answered("my-post", Ok restoredPost)) model
      Expect.equal (document' model).Fields.Title "First title" "The revision title"
      Expect.isTrue (document' model).PendingEdit "A pending edit"
      Expect.equal (document' model).Revisions Revisions.Closed "The dialog is closed"
      Expect.equal (Editor.statusText (document' model)) "Published Feb 4, 2026 · changes not live · saved" "The status"
    }

    test "a refused restore shows the reason, keeps the list, and lets the author try again" {
      let model, _ = send (Editor.RestoreChosen older.Name) (listed pendingPost)
      let model, _ = send (Editor.RestoreAnswered("my-post", Error Api.Stale)) model

      Expect.equal
        (document' model).Revisions
        (Revisions.RestoreFailed([ newer; older ], Api.reason Api.Stale))
        "The reason and the list"

      let _, effect = send (Editor.RestoreChosen older.Name) model
      Expect.isSome effect "A second call"
    }

    test "a list or a restore that the API refuses for the account signs the author out" {
      let model, _ = send Editor.RevisionsRequested (opened livePost)
      let next, _ = send (Editor.RevisionsAnswered("my-post", Error Api.Refused)) model
      Expect.notEqual next.Session model.Session "The list refusal"

      let model, _ = send (Editor.RestoreChosen older.Name) (listed livePost)
      let next, _ = send (Editor.RestoreAnswered("my-post", Error Api.Refused)) model
      Expect.notEqual next.Session model.Session "The restore refusal"
    }

    test "Close closes the dialog with no call" {
      let model, effect = send Editor.RevisionsClosed (listed livePost)
      Expect.equal (document' model).Revisions Revisions.Closed "Closed"
      Expect.isNone effect "No call"
    }

    test "the dialog is a layer over the editor, so Esc does not close the preview" {
      let model, _ = send Editor.PreviewToggled (opened livePost)
      let model, _ = send Editor.RevisionsRequested model
      let closed, _ = Shell.update (Shell.Editor Editor.PreviewClosed) model
      Expect.equal closed model "Esc leaves the preview open under the dialog"
    }

    test "the time text gives the hours and the minutes in UTC" {
      Expect.equal (Revisions.timeText "2026-02-01T10:05:00Z") "10:05 UTC" "The time"
      Expect.equal (Revisions.timeText "2026-02-01") "" "No time"
    }
  ]
