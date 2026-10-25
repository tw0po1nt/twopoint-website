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
  match document model with
  | Some document -> Editor.retryFailureText document
  | None -> ""

let private slug (model: Shell.Model) =
  match document model with
  | Some document -> document.Slug
  | None -> ""

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
  Publish.choice (publishModel model) |> Option.defaultValue Publish.Now

let private isOnDate (model: Shell.Model) =
  match publishChoice model with
  | Publish.OnDate _ -> true
  | Publish.Now -> false

let private typedDate (model: Shell.Model) =
  match publishChoice model with
  | Publish.OnDate typed -> typed
  | Publish.Now -> ""

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
    a (class' = "back", href = Route.path Posts, onClick = back) {
      Icon.view Icon.Back
      "Posts"
    }

    p (class' = "editor-status", role = "status", ariaLive = "polite") {
      statusText (model ())

      Show(when' = canRetryDeploy (model ())) {
        button (
          type' = "button",
          class' = "text deploy-retry",
          onClick = fun _ -> dispatch (Shell.Editor(Editor.DeployMsg(slug (model ()), Deploy.RetryClicked)))
        ) {
          "retry"
        }
      }

      Show(when' = (retryFailure (model ()) <> "")) { span (class' = "deploy-retry") { retryFailure (model ()) } }
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
      Parts.Alert(tone = Parts.Bad, title = "The preview did not load.").Has(p () { previewFailure (model ()) })
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
    Fragment() { Parts.Alert(tone = Parts.Bad, title = "The post did not load.").Has(p () { reason }) }
  | _ ->
    Fragment() {
      div (class' = "editor-column", ariaBusy = true) {
        Parts.skeleton "40%"
        Parts.skeleton "80%"
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
    h2 (id = "publish-dialog-title") { "Publish the post" }

    div (class' = "dialog-body") {
      p (class' = "muted small", role = "status") { Publish.statusText (publishModel (model ())) }

      div (class' = "publish-choices", role = "radiogroup", ariaLabel = "When to publish") {
        label (class' = "publish-choice") {
          input (
            type' = "radio",
            name = "publish-when",
            checked' = not (isOnDate (model ())),
            disabled = publishIsBusy (model ()),
            onChange = fun _ -> choose Publish.Now
          )

          "Now"
        }

        label (class' = "publish-choice") {
          input (
            type' = "radio",
            name = "publish-when",
            checked' = isOnDate (model ()),
            disabled = publishIsBusy (model ()),
            onChange = fun _ -> choose (Publish.OnDate(typedDate (model ())))
          )

          "On a date"
        }
      }

      Show(when' = isOnDate (model ())) {
        Parts
          .Field(id = "publish-date", label = "Date and time in UTC", hint = "Until then, the site shows the post with the Upcoming badge.")
          .Has(
            Parts
              .Input(
                id = "publish-date",
                kind = "datetime-local",
                value = (fun () -> typedDate (model ())),
                disabledWhen = (fun () -> publishIsBusy (model ())),
                onInput = fun event -> choose (Publish.OnDate((event.target :?> HTMLInputElement).value))
              )
              .View()
          )
      }

      publishProblems model

      Show(when' = (Publish.reason (publishModel (model ()))).IsSome) {
        Parts.Alert(tone = Parts.Bad, title = "The post is not published.").Has(
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
        .Has(Fragment() { "Publish" })
    }
  }

[<SolidComponent>]
let private publishNotice (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  Show(when' = not (List.isEmpty (publishModel (model ())).Warnings)) {
    Parts.Alert(tone = Parts.Warn, title = "The post is published, and a step after the publish failed.").Has(
      Fragment() {
        For(each = Array.ofList (publishModel (model ())).Warnings) { yield fun warning _ -> p () { Publish.warningText warning } }

        div (class' = "editor-notice-actions") {
          Parts.Button(size = Parts.Small, onClick = fun _ -> dispatch (Shell.Editor Editor.PublishNoticeDismissed)).Has(Fragment() { "Dismiss" })
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
      h1 (class' = "sr-only") { heading () }

      Show(when' = isLoaded (model ()), fallback = loading model) { writing dispatch bodyView model }
      Show(when' = isPreviewing (model ())) { preview model }
    }

    DetailsView.sheet dispatch model
    publishDialog dispatch model
  }
