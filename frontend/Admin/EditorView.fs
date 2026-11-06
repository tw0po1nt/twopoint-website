[<RequireQualifiedAccess>]
module TwoPoint.Admin.EditorView

open Browser
open Browser.Types
open Fable.Core.JsInterop
open Oxpecker.Solid
open Oxpecker.Solid.Aria

open System

let private document (model: Shell.Model) =
  match model.Editor with
  | Some(Editor.Loaded document) -> Some document
  | Some(Editor.Loading _)
  | Some(Editor.NotLoaded _)
  | None -> None

let focus (elementId: string) =
  window.setTimeout (
    (fun () -> Dom.document.getElementById elementId |> Option.ofObj |> Option.iter _.focus()),
    0
  )
  |> ignore

let moveToBodyLine (line: int) =
  window.setTimeout (
    (fun () ->
      Dom.document.querySelector ".editor-body"
      |> Option.ofObj
      |> Option.bind (fun host -> CodeMirror.EditorView.findFromDOM (host :?> HTMLElement) |> Option.ofObj)
      |> Option.iter (fun view -> CodeMirror.moveToLine view line)),
    0
  )
  |> ignore

let private hasProblems (model: Shell.Model) =
  match document model with
  | Some document -> Editor.hasProblems document
  | None -> false

[<SolidComponent>]
let private problemDot (model: unit -> Shell.Model) =
  Show(when' = hasProblems (model ())) {
    span (class' = "problem-dot", ariaHidden = true)
    span (class' = "sr-only") { ", has problems" }
  }

let private isLoaded (model: Shell.Model) = (document model).IsSome

let private statusText (model: Shell.Model) =
  match document model with
  | Some document -> Editor.statusText document
  | None -> ""

let private canRetryDeploy (model: Shell.Model) =
  match document model with
  | Some document -> Editor.canRetryDeploy document
  | None -> false

let private retryFailure (model: Shell.Model) =
  document model |> Option.bind Editor.retryFailureText

let private isTyping (model: Shell.Model) =
  match document model with
  | Some document -> document.Typing
  | None -> false

let private primaryIsAvailable (model: Shell.Model) =
  match document model with
  | Some document -> Editor.primaryAvailability document = Editor.Available
  | None -> false

let private menuItems (model: Shell.Model) =
  match document model with
  | Some document -> Array.ofList (Editor.menu document)
  | None -> [||]

let private publishModel (model: Shell.Model) =
  match document model with
  | Some document -> document.Publish
  | None -> Publish.init

let private publishChoice (model: Shell.Model) =
  match document model with
  | Some document -> Publish.choice document.Publish |> Option.defaultValue (Editor.firstChoice document)
  | None -> Publish.Draft Publish.Now

let private publishesChanges (model: Shell.Model) = Publish.isChanges (publishChoice model)

let private typedDate (model: Shell.Model) =
  match publishChoice model with
  | Publish.Draft(Publish.OnDate typed)
  | Publish.Changes(Publish.UpdateOn typed) -> Some typed
  | Publish.Draft Publish.Now
  | Publish.Changes Publish.KeepDate -> None

let private changedParts (model: Shell.Model) =
  match document model with
  | Some document -> Array.ofList (Editor.changesFromLive document)
  | None -> [||]

let private isChangesOpen (model: Shell.Model) =
  match document model with
  | Some document -> document.Changes = Editor.ChangesShown
  | None -> false

let private discardState (model: Shell.Model) =
  match document model with
  | Some document -> document.Discard
  | None -> Confirm.Closed

let private revisionsState (model: Shell.Model) =
  match document model with
  | Some document -> document.Revisions
  | None -> Revisions.Closed

let private hasPendingEdit (model: Shell.Model) =
  match document model with
  | Some document -> document.PendingEdit
  | None -> false

let private unpublishState (model: Shell.Model) =
  match document model with
  | Some document -> document.Unpublish
  | None -> Confirm.Closed

let private unpublishCheck (model: Shell.Model) =
  match document model with
  | Some document -> document.UnpublishCheck
  | None -> RemovalCheck.NotChecked

let private canUnpublish (model: Shell.Model) =
  match document model with
  | Some document -> Editor.canUnpublish document
  | None -> false

let private unpublishButtonText (model: Shell.Model) =
  match document model with
  | Some document -> Editor.unpublishButtonText document
  | None -> "Unpublish"

let private changeDateState (model: Shell.Model) =
  match document model with
  | Some document -> document.ChangeDate
  | None -> Typed.Closed

let private sendState (model: Shell.Model) =
  match document model with
  | Some document -> document.SendToSubscribers
  | None -> Typed.Closed

let private queuedCount (model: Shell.Model) =
  match document model with
  | Some document -> document.QueuedCount
  | None -> None

let private deleteStep (model: Shell.Model) =
  match document model with
  | Some document -> Editor.deleteStep document
  | None -> Editor.NoDeleteDialog

let private deleteConfirm (model: Shell.Model) =
  match document model with
  | Some document -> Editor.deleteConfirm document
  | None -> Editor.PlainConfirm

let private deleteReason (model: Shell.Model) =
  match document model with
  | Some document -> Editor.deleteReason document
  | None -> None

let private deleteCheck (model: Shell.Model) =
  match document model with
  | Some document -> document.DeleteCheck
  | None -> RemovalCheck.NotChecked

let private deleteButtonText (model: Shell.Model) =
  match document model with
  | Some document -> Editor.deleteButtonText document
  | None -> "Delete"

let private changeWarnings (model: Shell.Model) =
  match document model with
  | Some document -> Array.ofList document.ChangeWarnings
  | None -> [||]

let private publishIsBusy (model: Shell.Model) =
  match (publishModel model).State with
  | Publish.Saving
  | Publish.Checking _
  | Publish.Sending _ -> true
  | Publish.Closed
  | Publish.Choosing _
  | Publish.Checked _
  | Publish.CheckFailed _
  | Publish.Failed _ -> false

let private primaryText (model: Shell.Model) =
  match document model with
  | Some document -> Editor.primaryAction document
  | None -> "Publish"

let private fields (model: Shell.Model) =
  match document model with
  | Some document -> document.Fields
  | None -> { Title = ""; Excerpt = ""; Body = "" }

let private previewState (model: Shell.Model) =
  match document model with
  | Some document -> document.Preview
  | None -> Preview.Closed

let private isPreviewing (model: Shell.Model) = Preview.isOpen (previewState model)

let private isPreviewLoading (model: Shell.Model) = previewState model = Preview.Loading

let private previewFailure (model: Shell.Model) =
  match previewState model with
  | Preview.Failed reason -> reason
  | _ -> ""

let private previewProblems (model: Shell.Model) =
  match previewState model with
  | Preview.Shown preview -> Array.ofList preview.Problems
  | _ -> [||]

let private previewFrame (model: Shell.Model) =
  match previewState model with
  | Preview.Shown { Article = Some article } -> Preview.frame article
  | _ -> ""

let private hasPreviewFrame (model: Shell.Model) = previewFrame model <> ""

let private dropFiles (dispatch: Shell.Msg -> unit) (files: File array) (at: int) =
  for file in files do
    dispatch (
      Shell.Upload(
        Upload.Dropped
          { Name = file.name
            ContentType = file.``type``
            Size = file.size
            File = file
            At = at }
      )
    )

let private pickFiles (onPicked: File array -> unit) =
  let input = Browser.Dom.document.createElement "input" :?> HTMLInputElement
  input.``type`` <- "file"
  input.accept <- "image/jpeg,image/png,image/webp,image/gif,application/pdf"
  input.multiple <- true

  input.addEventListener (
    "change",
    fun _ -> onPicked [| for index in 0 .. int input.files.length - 1 -> input.files[index] |]
  )

  input.click ()

let private addImage (dispatch: Shell.Msg -> unit) (bodyView: CodeMirror.EditorView option ref) () =
  pickFiles (fun files ->
    let at = bodyView.Value |> Option.map CodeMirror.cursor |> Option.defaultValue 0
    dropFiles dispatch files at)

let private reconcile (dispatch: Shell.Msg -> unit) (view: CodeMirror.EditorView) (items: Upload.Item list) =
  for item in items do
    match item.State with
    | Upload.Uploading ->
      if not (CodeMirror.hasPlaceholder view item.Id) then
        CodeMirror.addPlaceholder view item.Id item.At item.Name
    | Upload.Ready url ->
      CodeMirror.replacePlaceholder
        view
        item.Id
        (Func<_, _, _>(fun before after ->
          let text, cursor = Upload.insertion item.Kind url before after
          createObj [ "text" ==> text; "cursor" ==> cursor ]))

      dispatch (Shell.Upload(Upload.Inserted item.Id))
    | Upload.Failed _ ->
      let at = CodeMirror.placeholderPosition view item.Id

      if at >= 0 then
        CodeMirror.removePlaceholder view item.Id
        dispatch (Shell.Upload(Upload.Placed(item.Id, at)))

let private fit (element: obj) =
  element?style?height <- "auto"

  if element?scrollHeight > 0. then
    element?style?height <- $"{element?scrollHeight}px"

let private follow (element: HTMLTextAreaElement) (value: unit -> string) =
  createEffect (fun () ->
    let text = value ()

    if element.value <> text then
      element.value <- text

    fit element)

let private typed (send: string -> unit) (event: InputEvent) =
  let element = event.target :?> HTMLTextAreaElement
  fit element
  send element.value

[<SolidComponent>]
let private menuEntry (dispatch: Shell.Msg -> unit) (item: Editor.MenuItem) =
  match item.Availability with
  | Editor.Available ->
    Parts.menuItem
      "editor-menu"
      Parts.Plain
      (fun () -> Editor.menuMsg item.Action |> Option.iter (Shell.Editor >> dispatch))
      (Fragment() { item.Label })
    :> HtmlElement
  | Editor.Unavailable reason -> Parts.menuUnavailable reason (Fragment() { item.Label }) :> HtmlElement

[<SolidComponent>]
let private topBar
  (dispatch: Shell.Msg -> unit)
  (bodyView: CodeMirror.EditorView option ref)
  (back: MouseEvent -> unit)
  (model: unit -> Shell.Model)
  =
  header (class' = (if isTyping (model ()) then "editor-bar is-typing" else "editor-bar")) {
    a (class' = "back", href = Route.path (Posts PostFilter.All), onClick = back) {
      Icon.view Icon.Back
      "Posts"
    }

    p (class' = "editor-status", role = "status", ariaLive = "polite") {
      statusText (model ())

      Show(when' = canRetryDeploy (model ())) {
        button (
          type' = "button",
          class' = "text deploy-retry",
          onClick =
            fun _ ->
              document (model ())
              |> Option.iter (fun document -> dispatch (Shell.Editor(Editor.DeployMsg(document.Slug, Deploy.RetryClicked))))
        ) {
          "retry"
        }
      }

      Show(when' = (retryFailure (model ())).IsSome) {
        span (class' = "deploy-retry") { retryFailure (model ()) |> Option.defaultValue "" }
      }
    }

    div (class' = "editor-actions") {
      button (
        type' = "button",
        class' = "text",
        disabled = not (isLoaded (model ())),
        ariaPressed = (if isPreviewing (model ()) then "true" else "false"),
        onClick = fun _ -> dispatch (Shell.Editor Editor.PreviewToggled)
      ) {
        "Preview"
      }

      button (type' = "button", class' = "text", disabled = not (isLoaded (model ())), onClick = fun _ -> DetailsView.open' dispatch) {
        "Details"
        problemDot model
      }
      button (
        type' = "button",
        class' = "editor-primary",
        disabled = not (primaryIsAvailable (model ())),
        onClick = fun _ -> dispatch (Shell.Editor Editor.PublishClicked)
      ) {
        primaryText (model ())
        problemDot model
      }

      button (
        type' = "button",
        class' = "text",
        ariaLabel = "More actions",
        ariaHasPopup = "menu",
        popovertarget = "editor-menu"
      ) {
        "•••"
      }

      Parts.menu
        "editor-menu"
        { Side = Menu.Below; Edge = Menu.End }
        (Fragment() {
          For(each = menuItems (model ())) { yield fun item _ -> menuEntry dispatch item }
          Parts.menuSeparator ()
          Parts.menuItem "editor-menu" Parts.Plain (addImage dispatch bodyView) (Fragment() { "Add image" })
        })
    }
  }

[<SolidComponent>]
let private uploadNotice (dispatch: Shell.Msg -> unit) (notice: Upload.Notice) =
  Parts.Alert(tone = Parts.Bad, title = notice.Title).Has(
    Fragment() {
      p () { notice.Reason }

      div (class' = "editor-notice-actions") {
        Show(when' = notice.CanRetry) {
          Parts.Button(size = Parts.Small, onClick = fun _ -> dispatch (Shell.Upload(Upload.Retried notice.Id))).Has(Fragment() { "Retry" })
        }

        Parts.Button(size = Parts.Small, onClick = fun _ -> dispatch (Shell.Upload(Upload.Dismissed notice.Id))).Has(Fragment() { "Dismiss" })
      }
    }
  )

[<SolidComponent>]
let private uploadNotices (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  div (class' = "editor-notices") {
    For(each = Array.ofList (Upload.notices (model ()).Uploads)) { yield fun notice _ -> uploadNotice dispatch notice }
  }

[<SolidComponent>]
let private writing (dispatch: Shell.Msg -> unit) (bodyView: CodeMirror.EditorView option ref) (model: unit -> Shell.Model) =
  let send (toMsg: string -> Editor.Msg) = toMsg >> Shell.Editor >> dispatch
  let excerpt: HTMLTextAreaElement option ref = ref None

  article (class' = (if isPreviewing (model ()) then "editor-column is-hidden" else "editor-column")) {
    textarea(
      id = "editor-title",
      class' = "editor-title",
      rows = 1,
      placeholder = "Title",
      ariaLabel = "Title",
      onInput = typed (send Editor.TitleChanged)
    )
      .on("keydown", fun event ->
        let key = event :?> KeyboardEvent

        if key.key = "Enter" then
          key.preventDefault ()
          excerpt.Value |> Option.iter (fun element -> element.focus ()))
      .ref (fun element -> follow element (fun () -> (fields (model ())).Title))

    textarea(
      id = "editor-excerpt",
      class' = "editor-excerpt",
      rows = 1,
      placeholder = "Excerpt",
      ariaLabel = "Excerpt",
      onInput = typed (send Editor.ExcerptChanged)
    )
      .ref (fun element ->
        excerpt.Value <- Some element
        follow element (fun () -> (fields (model ())).Excerpt))

    div(class' = "editor-body").ref (fun host ->
      let view =
        untrack (fun () ->
          CodeMirror.create
            host
            (fields (model ())).Body
            "Body"
            "Write the post in Markdown."
            (send Editor.BodyChanged)
            (Action<File array, int>(dropFiles dispatch)))

      bodyView.Value <- Some view

      createEffect (fun () ->
        let items = (model ()).Uploads.Items
        untrack (fun () -> reconcile dispatch view items))

      onCleanup (fun () ->
        bodyView.Value <- None
        view.destroy ()))

    uploadNotices dispatch model
  }

[<SolidComponent>]
let private preview (model: unit -> Shell.Model) =
  section (class' = "editor-preview", ariaLabel = "Preview") {
    Show(when' = isPreviewLoading (model ())) {
      div (class' = "editor-column", ariaBusy = true) {
        Parts.skeleton "40%"
        Parts.skeleton "80%"
      }
    }

    Show(when' = (previewFailure (model ()) <> "")) {
      Parts.Alert(tone = Parts.Bad, title = "Couldn't load the preview.").Has(p () { previewFailure (model ()) })
    }

    Show(when' = ((previewProblems (model ())).Length > 0)) {
      ul (class' = "preview-problems") {
        For(each = previewProblems (model ())) {
          yield fun problem _ ->
            li (class' = $"preview-problem {Preview.severityClass problem}") {
              span (class' = "preview-severity") { $"{Preview.severityText problem} " }
              Preview.problemText problem
            }
        }
      }
    }

    Show(when' = hasPreviewFrame (model ())) {
      iframe(
        class' = "preview-frame",
        title = "The post as readers see it",
        sandbox = "allow-same-origin",
        srcdoc = previewFrame (model ())
      )
    }
  }

[<SolidComponent>]
let private loading (model: unit -> Shell.Model) =
  match (model ()).Editor with
  | Some(Editor.NotLoaded reason) ->
    Fragment() { Parts.Alert(tone = Parts.Bad, title = "Couldn't load the post.").Has(p () { reason }) }
  | _ ->
    Fragment() {
      div (class' = "editor-column", ariaBusy = true) {
        Parts.skeleton "40%"
        Parts.skeleton "80%"
      }
    }

[<SolidComponent>]
let private diffEntry (entry: Diff.Entry) : HtmlElement =
  match entry with
  | Diff.Shown(Diff.Same text) ->
    div (class' = "diff-line") {
      span (class' = "diff-mark", ariaHidden = true) { " " }
      text
    }
  | Diff.Shown(Diff.Added text) ->
    div (class' = "diff-line diff-added") {
      span (class' = "diff-mark", ariaHidden = true) { "+" }
      span (class' = "sr-only") { "Added: " }
      text
    }
  | Diff.Shown(Diff.Removed text) ->
    div (class' = "diff-line diff-removed") {
      span (class' = "diff-mark", ariaHidden = true) { "-" }
      span (class' = "sr-only") { "Removed: " }
      text
    }
  | Diff.Skipped count -> div (class' = "diff-skip") { $"{count} unchanged lines" }

[<SolidComponent>]
let private noChanges () =
  p (class' = "muted small") { "No changes from the live version." }

[<SolidComponent>]
let private changesView (model: unit -> Shell.Model) =
  Show(when' = ((changedParts (model ())).Length > 0), fallback = noChanges ()) {
    div (class' = "diff") {
      For(each = changedParts (model ())) {
        yield fun part _ ->
          section () {
            h3 () { part.Name }

            div (class' = "diff-lines") {
              For(each = Array.ofList part.Entries) { yield fun entry _ -> diffEntry entry }
            }
          }
      }
    }
  }

[<SolidComponent>]
let private changesDialog (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  let close () = dispatch (Shell.Editor Editor.ChangesClosed)

  Parts.modalDialog
    "changes-dialog"
    (fun () -> isChangesOpen (model ()))
    close
    "Compare with live"
    (changesView model)
    (Fragment() { Parts.Button(onClick = fun _ -> close ()).Has(Fragment() { "Close" }) })

[<SolidComponent>]
let private discardDialog (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  let send msg = dispatch (Shell.Editor msg)

  dialog(id = "discard-dialog", class' = "dialog", ariaLabelledBy = "discard-dialog-title")
    .ref(Parts.followOpen (fun () -> Confirm.isOpen (discardState (model ()))))
    .on("cancel", fun event ->
      if Confirm.isSending (discardState (model ())) then
        event.preventDefault())
    .on ("close", fun _ -> send Editor.DiscardClosed) {
    h2 (id = "discard-dialog-title") { "Discard changes?" }

    div (class' = "dialog-body") {
      p () { "Your unpublished changes will be deleted, and the editor goes back to the live version. This can't be undone." }

      Show(when' = (Confirm.reason (discardState (model ()))).IsSome) {
        Parts.Alert(tone = Parts.Bad, title = "Couldn't discard the changes.").Has(
          p () { Confirm.reason (discardState (model ())) |> Option.defaultValue "" }
        )
      }
    }

    div (class' = "actions") {
      Parts.Button(disabled = Confirm.isSending (discardState (model ())), onClick = fun _ -> send Editor.DiscardClosed).Has(Fragment() { "Cancel" })

      Parts.Button(
        kind = Parts.Danger,
        disabled = Confirm.isSending (discardState (model ())),
        onClick = fun _ -> send Editor.DiscardConfirmed
      )
        .Has(Fragment() { "Discard" })
    }
  }

[<SolidComponent>]
let private revisionEntry (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) (revision: Api.Revision) =
  let restoring () = Revisions.isRestoring (revisionsState (model ()))
  let publishedText = $"{Editor.longDateText revision.PublishedAt}, {Revisions.timeText revision.PublishedAt}"

  li (class' = "revision") {
    div (class' = "grow") {
      p (class' = "revision-title") { revision.Title |> Option.defaultValue "No title" }
      p (class' = "muted small") { $"Published {publishedText}" }
    }

    Parts.Button(
      disabled = restoring (),
      label = $"Restore the revision of {publishedText}",
      onClick = fun _ -> dispatch (Shell.Editor(Editor.RestoreChosen revision.Name))
    )
      .Has(
        Fragment() {
          if Revisions.restoringName (revisionsState (model ())) = Some revision.Name then
            "Restoring…"
          else
            "Restore"
        }
      )
  }

[<SolidComponent>]
let private revisionsDialog (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  let send msg = dispatch (Shell.Editor msg)
  let state () = revisionsState (model ())

  dialog(id = "revisions-dialog", class' = "dialog", ariaLabelledBy = "revisions-dialog-title")
    .ref(Parts.followOpen (fun () -> Revisions.isOpen (state ())))
    .on("cancel", fun event ->
      if Revisions.isRestoring (state ()) then
        event.preventDefault())
    .on ("close", fun _ -> send Editor.RevisionsClosed) {
    h2 (id = "revisions-dialog-title") { "Revisions" }

    div (class' = "dialog-body") {
      p () {
        if hasPendingEdit (model ()) then
          "Every publish saves a revision. Restoring one replaces your unpublished changes. The live site doesn't change until you publish."
        else
          "Every publish saves a revision. Restoring one loads it into the editor. The live site doesn't change until you publish."
      }

      Show(when' = (state () = Revisions.Loading)) { p (class' = "muted small") { "Loading revisions…" } }

      Show(when' = (match state () with Revisions.Listed [] -> true | _ -> false)) {
        p (class' = "muted small") { "No revisions yet." }
      }

      ul (class' = "revision-list") {
        For(each = Array.ofList (Revisions.revisions (state ()))) { yield fun revision _ -> revisionEntry dispatch model revision }
      }

      Show(when' = (Revisions.reason (state ())).IsSome) {
        Parts.Alert(
          tone = Parts.Bad,
          title =
            (match state () with
             | Revisions.LoadFailed _ -> "Couldn't load the revisions."
             | _ -> "Couldn't restore the revision.")
        )
          .Has(p () { Revisions.reason (state ()) |> Option.defaultValue "" })
      }
    }

    div (class' = "actions") {
      Parts.Button(disabled = Revisions.isRestoring (state ()), onClick = fun _ -> send Editor.RevisionsClosed).Has(Fragment() { "Close" })
    }
  }

/// `blocked` is the title of the alert of a blocked check.
[<SolidComponent>]
let private removalCheckView (check: unit -> RemovalCheck.State) (blocked: string) =
  Fragment() {
    Show(when' = (check () = RemovalCheck.Checking)) {
      p (class' = "muted small", role = "status") { "Checking for links to this post…" }
    }

    Show(when' = not (List.isEmpty (RemovalCheck.edits (check ())))) {
      p () {
        "Other posts link here. Their unpublished changes remove those links and will be published first, in the same rebuild."
      }

      ul (class' = "removal-edits", ariaLabel = "Pending edits to publish") {
        For(each = Array.ofList (RemovalCheck.edits (check ()))) { yield fun edit _ -> li () { edit.Title } }
      }
    }

    Show(when' = not (List.isEmpty (RemovalCheck.problems (check ())))) {
      Parts.Alert(tone = Parts.Bad, title = blocked).Has(
        ul (class' = "preview-problems", ariaLabel = "Problems") {
          For(each = Array.ofList (RemovalCheck.problems (check ()))) {
            yield fun problem _ -> li (class' = "preview-problem") { Preview.problemText problem }
          }
        }
      )
    }

    Show(when' = (RemovalCheck.failure (check ())).IsSome) {
      Parts.Alert(tone = Parts.Bad, title = "Couldn't check for links to this post. Close the dialog and try again.").Has(
        p () { RemovalCheck.failure (check ()) |> Option.defaultValue "" }
      )
    }
  }

[<SolidComponent>]
let private unpublishDialog (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  let send msg = dispatch (Shell.Editor msg)

  dialog(id = "unpublish-dialog", class' = "dialog", ariaLabelledBy = "unpublish-dialog-title")
    .ref(Parts.followOpen (fun () -> Confirm.isOpen (unpublishState (model ()))))
    .on("cancel", fun event ->
      if Confirm.isSending (unpublishState (model ())) then
        event.preventDefault())
    .on ("close", fun _ -> send Editor.UnpublishClosed) {
    h2 (id = "unpublish-dialog-title") { "Unpublish this post?" }

    div (class' = "dialog-body") {
      p () { "The post goes back to being a draft and comes off the site. Any unpublished changes are kept in the draft." }

      removalCheckView (fun () -> unpublishCheck (model ())) "Can't unpublish: other posts still link here."

      Show(when' = (Confirm.reason (unpublishState (model ()))).IsSome) {
        Parts.Alert(tone = Parts.Bad, title = "Couldn't unpublish the post.").Has(
          p () { Confirm.reason (unpublishState (model ())) |> Option.defaultValue "" }
        )
      }
    }

    div (class' = "actions") {
      Parts.Button(disabled = Confirm.isSending (unpublishState (model ())), onClick = fun _ -> send Editor.UnpublishClosed).Has(Fragment() { "Cancel" })

      Parts.Button(kind = Parts.Danger, disabled = not (canUnpublish (model ())), onClick = fun _ -> send Editor.UnpublishConfirmed)
        .Has(Fragment() { unpublishButtonText (model ()) })
    }
  }

[<SolidComponent>]
let private deleteDialog (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  let send msg = dispatch (Shell.Editor msg)
  let sending () = deleteStep (model ()) = Editor.DeleteRuns

  dialog(id = "delete-dialog", class' = "dialog", ariaLabelledBy = "delete-dialog-title")
    .ref(Parts.followOpen (fun () -> deleteStep (model ()) <> Editor.NoDeleteDialog))
    .on("cancel", fun event ->
      if sending () then
        event.preventDefault())
    .on ("close", fun _ -> send Editor.DeleteClosed) {
    h2 (id = "delete-dialog-title") { "Delete this post?" }

    div (class' = "dialog-body") {
      Show(
        when' = (deleteConfirm (model ()) = Editor.TypeSlug),
        fallback = p () { "This draft will be deleted. This can't be undone." }
      ) {
        p () { "This deletes the post along with its unpublished changes, revisions, and comments, then rebuilds the site without it. Delivery records are kept. This can't be undone." }

        removalCheckView (fun () -> deleteCheck (model ())) "Can't delete: other posts still link here."

        Parts
          .Field(
            id = "delete-slug",
            label = $"""Type {(match document (model ()) with Some document -> document.Slug | None -> "the slug")} to confirm"""
          )
          .Has(
            Parts
              .Input(
                id = "delete-slug",
                value = (fun () -> match document (model ()) with Some document -> Typed.typed document.Delete | None -> ""),
                disabledWhen = sending,
                onInput = (fun event -> send (Editor.DeleteTyped (event.target :?> HTMLInputElement).value)),
                onEnter = fun () -> send Editor.DeleteConfirmed
              )
              .View()
          )
      }

      Show(when' = (deleteReason (model ())).IsSome) {
        Parts.Alert(tone = Parts.Bad, title = "Couldn't delete the post.").Has(
          p () { deleteReason (model ()) |> Option.defaultValue "" }
        )
      }
    }

    div (class' = "actions") {
      Parts.Button(disabled = sending (), onClick = fun _ -> send Editor.DeleteClosed).Has(Fragment() { "Cancel" })

      Parts.Button(
        kind = Parts.Danger,
        disabled = (deleteStep (model ()) <> Editor.ReadyToDelete),
        onClick = fun _ -> send Editor.DeleteConfirmed
      )
        .Has(Fragment() { deleteButtonText (model ()) })
    }
  }

[<SolidComponent>]
let private changeDateDialog (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  let send msg = dispatch (Shell.Editor msg)
  let state () = changeDateState (model ())
  let sending () = Typed.isSending (state ())

  dialog(id = "change-date-dialog", class' = "dialog publish-dialog", ariaLabelledBy = "change-date-dialog-title")
    .ref(Parts.followOpen (fun () -> Typed.isOpen (state ())))
    .on("cancel", fun event ->
      if sending () then
        event.preventDefault())
    .on ("close", fun _ -> send Editor.ChangeDateClosed) {
    h2 (id = "change-date-dialog-title") { "Change date" }

    div (class' = "dialog-body") {
      p (class' = "muted small", role = "status") { ChangeDate.statusText (state ()) }

      Parts
        .Field(id = "new-publish-date", label = "Date and time in UTC", hint = "Until then, the post shows on the site as Upcoming.")
        .Has(
          Parts
            .Input(
              id = "new-publish-date",
              kind = "datetime-local",
              value = (fun () -> Typed.typed (state ())),
              disabledWhen = sending,
              onInput = fun event -> send (Editor.ChangeDateTyped (event.target :?> HTMLInputElement).value)
            )
            .View()
        )

      Show(when' = (Typed.reason (state ())).IsSome) {
        Parts.Alert(tone = Parts.Bad, title = "Couldn't change the date.").Has(
          p () { Typed.reason (state ()) |> Option.defaultValue "" }
        )
      }
    }

    div (class' = "actions") {
      Parts.Button(disabled = sending (), onClick = fun _ -> send Editor.ChangeDateClosed).Has(Fragment() { "Cancel" })

      Parts.Button(
        kind = Parts.Primary,
        disabled = not (ChangeDate.canConfirm (state ())),
        onClick = fun _ -> send Editor.ChangeDateConfirmed
      )
        .Has(Fragment() { "Change date" })
    }
  }

[<SolidComponent>]
let private sendDialog (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  let send msg = dispatch (Shell.Editor msg)
  let state () = sendState (model ())
  let sending () = Typed.isSending (state ())

  dialog(id = "send-dialog", class' = "dialog publish-dialog", ariaLabelledBy = "send-dialog-title")
    .ref(Parts.followOpen (fun () -> Typed.isOpen (state ())))
    .on("cancel", fun event ->
      if sending () then
        event.preventDefault())
    .on ("close", fun _ -> send Editor.SendClosed) {
    h2 (id = "send-dialog-title") { "Send to subscribers?" }

    div (class' = "dialog-body") {
      p () { "Emails this post to every active subscriber who hasn't already received it." }

      Parts
        .Field(id = "send-intro", label = "Intro", hint = "Optional. Appears above the post in the email.")
        .Has(
          Parts
            .Textarea(
              id = "send-intro",
              value = (fun () -> Typed.typed (state ())),
              disabled = sending (),
              onInput = fun event -> send (Editor.IntroTyped (event.target :?> HTMLTextAreaElement).value)
            )
            .View()
        )

      Show(when' = (Typed.reason (state ())).IsSome) {
        Parts.Alert(tone = Parts.Bad, title = "Couldn't send the post.").Has(
          p () { Typed.reason (state ()) |> Option.defaultValue "" }
        )
      }
    }

    div (class' = "actions") {
      Parts.Button(disabled = sending (), onClick = fun _ -> send Editor.SendClosed).Has(Fragment() { "Cancel" })

      Parts.Button(kind = Parts.Primary, disabled = sending (), onClick = fun _ -> send Editor.SendConfirmed)
        .Has(Fragment() { "Send" })
    }
  }

[<SolidComponent>]
let private publishProblems (model: unit -> Shell.Model) =
  Show(when' = not (List.isEmpty (Publish.problems (publishModel (model ()))))) {
    ul (class' = "preview-problems", ariaLabel = "Problems") {
      For(each = Array.ofList (Publish.problems (publishModel (model ())))) {
        yield fun problem _ ->
          li (class' = $"preview-problem {Preview.severityClass problem}") {
            span (class' = "preview-severity") { $"{Preview.severityText problem} " }
            Preview.problemText problem
          }
      }
    }
  }

type private Choices =
  { Group: string
    FieldId: string
    FieldLabel: string
    Hint: string
    Plain: string * Publish.When
    Dated: string * (string -> Publish.When) }

[<SolidComponent>]
let private publishChoices (model: unit -> Shell.Model) (choose: Publish.When -> unit) (choices: Choices) =
  let plainLabel, plain = choices.Plain
  let datedLabel, dated = choices.Dated
  let isDated () = (typedDate (model ())).IsSome
  let busy () = publishIsBusy (model ())

  div () {
    div (class' = "publish-choices", role = "radiogroup", ariaLabel = choices.Group) {
      label (class' = "publish-choice") {
        input (
          type' = "radio",
          name = "publish-when",
          checked' = not (isDated ()),
          disabled = busy (),
          onChange = fun _ -> choose plain
        )

        plainLabel
      }

      label (class' = "publish-choice") {
        input (
          type' = "radio",
          name = "publish-when",
          checked' = isDated (),
          disabled = busy (),
          onChange = fun _ -> choose (dated (typedDate (model ()) |> Option.defaultValue ""))
        )

        datedLabel
      }
    }

    Show(when' = isDated ()) {
      Parts
        .Field(id = choices.FieldId, label = choices.FieldLabel, hint = choices.Hint)
        .Has(
          Parts
            .Input(
              id = choices.FieldId,
              kind = "datetime-local",
              value = (fun () -> typedDate (model ()) |> Option.defaultValue ""),
              disabledWhen = busy,
              onInput = fun event -> choose (dated (event.target :?> HTMLInputElement).value)
            )
            .View()
        )
    }
  }

[<SolidComponent>]
let private publishDialog (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  let send msg = dispatch (Shell.Editor msg)
  let choose (choice: Publish.When) = send (Editor.PublishChoiceChanged choice)

  dialog(id = "publish-dialog", class' = "dialog publish-dialog", ariaLabelledBy = "publish-dialog-title")
    .ref(Parts.followOpen (fun () -> Publish.isOpen (publishModel (model ()))))
    .on("cancel", fun event ->
      if Publish.isSending (publishModel (model ())) then
        event.preventDefault())
    .on ("close", fun _ -> send Editor.PublishClosed) {
    h2 (id = "publish-dialog-title") { if publishesChanges (model ()) then "Publish changes" else "Publish post" }

    div (class' = "dialog-body") {
      p (class' = "muted small", role = "status") { Publish.statusText (publishModel (model ())) }

      Show(when' = publishesChanges (model ())) {
        div () {
          changesView model

          publishChoices
            model
            choose
            { Group = "Update date"
              FieldId = "update-date"
              FieldLabel = "Update date and time in UTC"
              Hint = "Shown on the post as its updated date."
              Plain = "Keep the current update date", Publish.Changes Publish.KeepDate
              Dated = "Set a new update date", (Publish.UpdateOn >> Publish.Changes) }
        }
      }

      Show(when' = not (publishesChanges (model ()))) {
        publishChoices
          model
          choose
          { Group = "When to publish"
            FieldId = "publish-date"
            FieldLabel = "Date and time in UTC"
            Hint = "Until then, the post shows on the site as Upcoming."
            Plain = "Now", Publish.Draft Publish.Now
            Dated = "On a date", (Publish.OnDate >> Publish.Draft) }
      }

      publishProblems model

      Show(when' = (Publish.reason (publishModel (model ()))).IsSome) {
        Parts.Alert(tone = Parts.Bad, title = (if publishesChanges (model ()) then "Couldn't publish the changes." else "Couldn't publish the post.")).Has(
          p () { Publish.reason (publishModel (model ())) |> Option.defaultValue "" }
        )
      }
    }

    div (class' = "actions") {
      Parts.Button(disabled = Publish.isSending (publishModel (model ())), onClick = fun _ -> send Editor.PublishClosed).Has(Fragment() { "Cancel" })

      Parts.Button(
        kind = Parts.Primary,
        disabled = not (Publish.canConfirm (publishModel (model ()))),
        onClick = fun _ -> send Editor.PublishConfirmed
      )
        .Has(Fragment() { if publishesChanges (model ()) then "Publish changes" else "Publish" })
    }
  }

[<SolidComponent>]
let private publishNotice (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  Show(when' = not (List.isEmpty (publishModel (model ())).Warnings)) {
    Parts.Alert(tone = Parts.Warn, title = "Published, with problems").Has(
      Fragment() {
        For(each = Array.ofList (publishModel (model ())).Warnings) { yield fun warning _ -> p () { Publish.warningText warning } }

        div (class' = "editor-notice-actions") {
          Parts.Button(size = Parts.Small, onClick = fun _ -> dispatch (Shell.Editor Editor.PublishNoticeDismissed)).Has(Fragment() { "Dismiss" })
        }
      }
    )
  }

[<SolidComponent>]
let private changeNotice (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  Show(when' = ((changeWarnings (model ())).Length > 0)) {
    Parts.Alert(tone = Parts.Warn, title = "Done, with problems").Has(
      Fragment() {
        For(each = changeWarnings (model ())) { yield fun warning _ -> p () { Publish.changeWarningText warning } }

        div (class' = "editor-notice-actions") {
          Parts.Button(size = Parts.Small, onClick = fun _ -> dispatch (Shell.Editor Editor.ChangeNoticeDismissed)).Has(Fragment() { "Dismiss" })
        }
      }
    )
  }

[<SolidComponent>]
let private sentNotice (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  Show(when' = (queuedCount (model ())).IsSome) {
    Parts.Alert(tone = Parts.Good, title = "Sending to subscribers").Has(
      Fragment() {
        p () { queuedCount (model ()) |> Option.map Editor.queuedText |> Option.defaultValue "" }

        div (class' = "editor-notice-actions") {
          Parts.Button(size = Parts.Small, onClick = fun _ -> dispatch (Shell.Editor Editor.SentNoticeDismissed)).Has(Fragment() { "Dismiss" })
        }
      }
    )
  }

[<SolidComponent>]
let page (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) (back: MouseEvent -> unit) (heading: unit -> string) =
  let listen (name: string) (handler: Event -> unit) =
    window.addEventListener (name, handler)
    onCleanup (fun () -> window.removeEventListener (name, handler))

  listen "keydown" (fun event ->
    let key = event :?> KeyboardEvent

    if (key.metaKey || key.ctrlKey) && key.key.ToLowerInvariant() = "s" then
      key.preventDefault ()
      dispatch (Shell.Editor Editor.SaveRequested)
    elif key.key = "Escape" && isPreviewing (model ()) then
      dispatch (Shell.Editor Editor.PreviewClosed))

  let bodyView: CodeMirror.EditorView option ref = ref None

  let fade (_: Event) =
    if isTyping (model ()) then
      dispatch (Shell.Editor Editor.PointerMoved)

  listen "pointermove" fade
  listen "touchstart" fade

  listen "beforeunload" (fun event ->
    match (model ()).Editor with
    | Some editor when Editor.isAtRisk editor -> event.preventDefault ()
    | _ -> ())

  Fragment() {
    topBar dispatch bodyView back model

    main (class' = "screen editor-screen") {
      Banner.view dispatch (model ()).Session
      publishNotice dispatch model
      changeNotice dispatch model
      sentNotice dispatch model
      h1 (class' = "sr-only") { heading () }

      Show(when' = isLoaded (model ()), fallback = loading model) { writing dispatch bodyView model }
      Show(when' = isPreviewing (model ())) { preview model }
    }

    DetailsView.sheet dispatch model
    publishDialog dispatch model
    changesDialog dispatch model
    discardDialog dispatch model
    revisionsDialog dispatch model
    unpublishDialog dispatch model
    changeDateDialog dispatch model
    sendDialog dispatch model
    deleteDialog dispatch model
  }
