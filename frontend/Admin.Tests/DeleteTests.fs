module TwoPoint.Admin.Tests.DeleteTests

open TwoPoint.Admin

open Expecto

let private noDetails: Api.PostDetails =
  { Author = None
    Category = None
    Tags = []
    Series = None
    Image = None
    ShowOnHomepage = true }

let private post state pendingEdit eTag: Api.PostDocument =
  { Slug = "my-post"
    State = state
    PendingEdit = pendingEdit
    PublishDate = if state = Api.Draft then None else Some "2026-02-04T13:00:00Z"
    ETag = eTag
    Title = "Title"
    Excerpt = "About it."
    Body = "Body.\n"
    Details = noDetails
    DeliveryStarted = None
    Text = ""
    Published =
      if state = Api.Draft then
        None
      else
        Some
          { Title = "Title"
            Excerpt = "About it."
            Body = "Body.\n"
            Details = noDetails } }

let private draft = post Api.Draft false (Some "\"w1\"")
let private live = post Api.Published false None
let private pending = post Api.Published true (Some "\"w2\"")

let private opened (document: Api.PostDocument) =
  let model, _ = Shell.init "/posts/my-post"
  let model, _ = Shell.update (Shell.Session(Session.Started true)) model
  fst (Shell.update (Shell.Editor(Editor.Answered("my-post", Ok document))) model)

let private send msg (model: Shell.Model) = Shell.update (Shell.Editor msg) model

let private document' (model: Shell.Model) =
  match model.Editor with
  | Some(Editor.Loaded document) -> document
  | other -> failtestf "Expected a loaded editor, got %A" other

let private typing text model = fst (send (Editor.DeleteTyped text) model)

let private requested model = fst (send Editor.DeleteRequested model)

let private checkedAs outcome model =
  fst (send (Editor.RemovalCheckAnswered("my-post", Editor.DeleteDialog, Ok outcome)) model)

/// The dialog is open, and the check found no link to the post.
let private ready model = checkedAs Api.PlainRemoval (requested model)

let private edit slug title: Api.RemovalEdit = { Slug = slug; Title = title; ETag = $"\"{slug}-w\"" }

let private problem message: Api.Problem =
  { Severity = Api.Severity.Error
    Place = None
    Message = message }

let private step model = Editor.deleteStep (document' model)

let private deleteItem model =
  Editor.menu (document' model) |> List.find (fun item -> item.Action = Editor.Delete)

[<Tests>]
let tests =
  testList "Delete a post" [
    test "the menu offers the delete of each saved post, and the message opens the dialog" {
      for document in [ draft; live; pending ] do
        Expect.equal (deleteItem (opened document)).Availability Editor.Available "Available"

      Expect.equal (Editor.menuMsg Editor.Delete) (Some Editor.DeleteRequested) "The message"
    }

    test "the menu gives the reason when the delete is not available" {
      let reason model =
        match (deleteItem model).Availability with
        | Editor.Unavailable reason -> reason
        | Editor.Available -> ""

      let model, _ = send (Editor.BodyChanged "More.\n") (opened live)
      Expect.equal (reason model) "Save the post first." "Unsaved text"
      let saving, _ = send Editor.SaveRequested model
      Expect.equal (reason saving) "The save runs." "Save runs"
      let failed, _ = send (Editor.SaveAnswered("my-post", 1, Error Api.Unanswered)) saving
      Expect.equal (reason failed) "Save the post first." "Failed save"
      let stale, _ = send (Editor.SaveAnswered("my-post", 1, Error Api.Stale)) saving
      Expect.equal (reason stale) "Another tab saved newer text. Reload the page." "Stale save"
      Expect.equal (reason (opened { draft with ETag = None })) "Reload the page, and then delete the post." "No ETag"
    }

    test "a published post asks for the slug, and a draft asks for a plain confirm" {
      Expect.equal (Editor.deleteConfirm (document' (opened live))) Editor.TypeSlug "Published"
      Expect.equal (Editor.deleteConfirm (document' (opened pending))) Editor.TypeSlug "Pending edit"
      Expect.equal (Editor.deleteConfirm (document' (opened draft))) Editor.PlainConfirm "Draft"
    }

    test "the delete of a published post stays disabled until the typed text equals the slug" {
      let model = ready (opened live)
      Expect.equal (step model) Editor.WaitingForSlug "No text yet"

      for text in [ "my"; "my-pos"; "my-post "; "My-Post"; "my-posts" ] do
        Expect.equal (step (typing text model)) Editor.WaitingForSlug $"Disabled for '{text}'"

      Expect.equal (step (typing "my-post" model)) Editor.ReadyToDelete "Enabled for the slug"
    }

    test "a confirm before the slug matches sends nothing" {
      let model, effect = send Editor.DeleteConfirmed (typing "my" (ready (opened live)))
      Expect.isNone effect "No call"
      Expect.equal (step model) Editor.WaitingForSlug "Still waiting"
    }

    test "the delete of a published post sends the typed text and no ETag when there is no pending edit" {
      let model, effect = send Editor.DeleteConfirmed (typing "my-post" (ready (opened live)))
      Expect.equal (step model) Editor.DeleteRuns "Sending"

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.DeletePost { Slug = "my-post"; ETag = None; ConfirmSlug = Some "my-post"; Edits = [] } ]))
        "The call"
    }

    test "the delete of a published post with a pending edit sends the ETag of the pending edit" {
      let _, effect = send Editor.DeleteConfirmed (typing "my-post" (ready (opened pending)))

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.DeletePost { Slug = "my-post"; ETag = Some "\"w2\""; ConfirmSlug = Some "my-post"; Edits = [] } ]))
        "The call"
    }

    test "the delete of a draft needs no typed text, and sends the ETag only" {
      let model = requested (opened draft)
      Expect.equal (step model) Editor.ReadyToDelete "Enabled at once"
      let _, effect = send Editor.DeleteConfirmed model

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.DeletePost { Slug = "my-post"; ETag = Some "\"w1\""; ConfirmSlug = None; Edits = [] } ]))
        "The call"
    }

    test "Keep closes the dialog with no call, but not while the delete is in flight" {
      for document in [ live; draft ] do
        let model = typing "my-post" (ready (opened document))
        let closed, effect = send Editor.DeleteClosed model
        Expect.equal (step closed) Editor.NoDeleteDialog "Closed"
        Expect.isNone effect "No call"

        let sending, _ = send Editor.DeleteConfirmed model
        let still, _ = send Editor.DeleteClosed sending
        Expect.equal (step still) Editor.DeleteRuns "Still sending"
    }

    test "a delete that succeeded goes to the post list, and the post list shows the warnings" {
      let model, _ = send Editor.DeleteConfirmed (typing "my-post" (ready (opened live)))
      let answered: Api.DeletedPost = { Warnings = [ Api.CommentsKept ] }
      let model, _ = send (Editor.DeleteAnswered("my-post", Ok answered)) model
      Expect.equal model.Router.Route (Route.Posts PostFilter.All) "The post list"
      Expect.equal model.Posts.Notice [ Api.CommentsKept ] "The warnings"

      let model, _ = Shell.update (Shell.Posts Posts.NoticeDismissed) model
      Expect.isEmpty model.Posts.Notice "Dismissed"
    }

    test "a refused delete shows the reason, keeps the typed text, and lets the author try again" {
      let reason = "If you delete the post, a link in another post breaks. The admin does not delete the post."
      let model, _ = send Editor.DeleteConfirmed (typing "my-post" (ready (opened live)))
      let model, effect = send (Editor.DeleteAnswered("my-post", Error(Api.Failed reason))) model
      Expect.equal (Editor.deleteReason (document' model)) (Some reason) "The reason"
      Expect.equal model.Router.Route (Route.Post "my-post") "The editor stays"
      Expect.equal effect (Some(Shell.Send [ Shell.CheckRemoval("my-post", Editor.DeleteDialog) ])) "The check runs again"
      Expect.equal (step model) Editor.CheckingDelete "Disabled during the check"
      let model = checkedAs Api.PlainRemoval model
      Expect.equal (step model) Editor.ReadyToDelete "Enabled again"
      let _, effect = send Editor.DeleteConfirmed model
      Expect.isSome effect "A second call"
    }

    test "a refused delete of a draft shows the reason" {
      let model, _ = send Editor.DeleteConfirmed (requested (opened draft))
      let model, _ = send (Editor.DeleteAnswered("my-post", Error(Api.Failed "The post changed since you opened it"))) model
      Expect.equal (Editor.deleteReason (document' model)) (Some "The post changed since you opened it") "The reason"
      Expect.equal (step model) Editor.ReadyToDelete "Enabled again"
    }

    test "a delete that the API refuses for the account signs the author out" {
      let model, _ = send Editor.DeleteConfirmed (typing "my-post" (ready (opened live)))
      let next, _ = send (Editor.DeleteAnswered("my-post", Error Api.Refused)) model
      Expect.notEqual next.Session model.Session "The session changes"
    }

    test "the dialog is a layer over the editor" {
      let model = requested (opened live)
      Expect.isTrue (Editor.isDeleteOpen (document' model)) "Open"
      Expect.isFalse (Editor.isDeleteOpen (document' (fst (send Editor.DeleteClosed model)))) "Closed"
    }

    test "the dialog of a published post runs the check one time when it opens" {
      let model, effect = send Editor.DeleteRequested (opened live)
      Expect.equal effect (Some(Shell.Send [ Shell.CheckRemoval("my-post", Editor.DeleteDialog) ])) "The check"
      Expect.equal (step model) Editor.CheckingDelete "The check runs"
      Expect.equal (step (typing "my-post" model)) Editor.CheckingDelete "The typed slug waits for the check"
      let _, confirmed = send Editor.DeleteConfirmed (typing "my-post" model)
      Expect.isNone confirmed "No delete during the check"
      let _, typedEffect = send (Editor.DeleteTyped "my-post") model
      Expect.isNone typedEffect "No second check"
    }

    test "the dialog of a draft runs no check" {
      let _, effect = send Editor.DeleteRequested (opened draft)
      Expect.isNone effect "No check"
    }

    test "a plain delete shows the Delete button and sends no edits" {
      let model = typing "my-post" (ready (opened live))
      Expect.equal (Editor.deleteButtonText (document' model)) "Delete" "The button"
      Expect.isEmpty (Editor.deleteEdits (document' model)) "No edits"
      Expect.equal (step model) Editor.ReadyToDelete "Enabled"
    }

    test "a publish and delete lists the posts, gives the number of edits on the button, and sends each edit with its ETag" {
      let edits = [ edit "a-post" "A post"; edit "c-post" "C post" ]
      let model = typing "my-post" (checkedAs (Api.PublishAndRemove edits) (requested (opened live)))
      Expect.equal (Editor.deleteEdits (document' model)) edits "The posts"
      Expect.equal (Editor.deleteButtonText (document' model)) "Publish 2 edits and delete" "The button"
      Expect.equal (step model) Editor.ReadyToDelete "Enabled"
      let one = checkedAs (Api.PublishAndRemove [ edit "a-post" "A post" ]) (requested (opened live))
      Expect.equal (Editor.deleteButtonText (document' one)) "Publish 1 edit and delete" "One edit"

      let _, effect = send Editor.DeleteConfirmed model

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.DeletePost { Slug = "my-post"; ETag = None; ConfirmSlug = Some "my-post"; Edits = edits } ]))
        "The call"
    }

    test "a publish and delete still needs the typed slug" {
      let model = checkedAs (Api.PublishAndRemove [ edit "a-post" "A post" ]) (requested (opened live))
      Expect.equal (step (typing "my" model)) Editor.WaitingForSlug "Disabled"
    }

    test "a blocked check shows the problems and disables the delete, also with the typed slug" {
      let problems = [ problem "The post 'c-post' links to the post and has no pending edit." ]
      let model = typing "my-post" (checkedAs (Api.Blocked problems) (requested (opened live)))
      Expect.equal (step model) Editor.DeleteBlocked "Disabled"
      Expect.equal (Editor.deleteProblems (document' model)) problems "The problems"
      Expect.equal (Editor.deleteButtonText (document' model)) "Delete" "The button"
      let _, effect = send Editor.DeleteConfirmed model
      Expect.isNone effect "No call"
    }

    test "a check that fails disables the delete and gives the reason" {
      let model = fst (send (Editor.RemovalCheckAnswered("my-post", Editor.DeleteDialog, Error Api.Unanswered)) (requested (opened live)))
      let model = typing "my-post" model
      Expect.equal (step model) Editor.DeleteBlocked "Disabled"
      Expect.equal (document' model).DeleteCheck (RemovalCheck.Failed "The API did not answer.") "The reason"
    }

    test "a refused delete runs the check again, and the dialog shows the new outcome" {
      let model = typing "my-post" (checkedAs (Api.PublishAndRemove [ edit "a-post" "A post" ]) (requested (opened live)))
      let model, _ = send Editor.DeleteConfirmed model
      let model, effect = send (Editor.DeleteAnswered("my-post", Error(Api.Failed "The pending edits changed."))) model
      Expect.equal effect (Some(Shell.Send [ Shell.CheckRemoval("my-post", Editor.DeleteDialog) ])) "The check runs again"
      let problems = [ problem "A link stays." ]
      let model = checkedAs (Api.Blocked problems) model
      Expect.equal (step model) Editor.DeleteBlocked "The new outcome"
      Expect.equal (Editor.deleteProblems (document' model)) problems "The problems"
      Expect.equal (Typed.typed (document' model).Delete) "my-post" "The typed slug stays"
    }

    test "a check answer after the dialog closes changes nothing" {
      let model = requested (opened live)
      let closed, _ = send Editor.DeleteClosed model
      let late = checkedAs Api.PlainRemoval closed
      Expect.equal (step late) Editor.NoDeleteDialog "Closed"
      Expect.equal (document' late).DeleteCheck RemovalCheck.NotChecked "No check"
    }

    test "a check that the API refuses for the account signs the author out" {
      let model = requested (opened live)
      let next, _ = send (Editor.RemovalCheckAnswered("my-post", Editor.DeleteDialog, Error Api.Refused)) model
      Expect.notEqual next.Session model.Session "The session changes"
    }
  ]
