[<RequireQualifiedAccess>]
module TwoPoint.Admin.EditorView

open Browser
open Browser.Types
open Fable.Core.JsInterop
open Oxpecker.Solid
open Oxpecker.Solid.Aria

let private document (model: Shell.Model) =
  match model.Editor with
  | Some(Editor.Loaded document) -> Some document
  | Some(Editor.Loading _)
  | Some(Editor.NotLoaded _)
  | None -> None

let private isLoaded (model: Shell.Model) = (document model).IsSome

let private statusText (model: Shell.Model) =
  match document model with
  | Some document -> Editor.statusText document
  | None -> ""

let private isTyping (model: Shell.Model) =
  match document model with
  | Some document -> document.Typing
  | None -> false

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
let private topBar (dispatch: Shell.Msg -> unit) (back: MouseEvent -> unit) (model: unit -> Shell.Model) =
  header (class' = (if isTyping (model ()) then "editor-bar is-typing" else "editor-bar")) {
    a (class' = "back", href = Route.path Posts, onClick = back) {
      Icon.view Icon.Back
      "Posts"
    }

    p (class' = "editor-status", role = "status", ariaLive = "polite") { statusText (model ()) }

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
      }
      // TODO(https://github.com/tw0po1nt/twopoint-website-staging/issues/259): Publish
      button (type' = "button", class' = "editor-primary", disabled = true) { primaryText (model ()) }
      // TODO(https://github.com/tw0po1nt/twopoint-website-staging/issues/261): the actions menu
      // TODO(https://github.com/tw0po1nt/twopoint-website-staging/issues/262): the actions menu
      // TODO(https://github.com/tw0po1nt/twopoint-website-staging/issues/263): the actions menu
      button (type' = "button", class' = "text", disabled = true, ariaLabel = "More actions") { "•••" }
    }
  }

[<SolidComponent>]
let private writing (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  let send (toMsg: string -> Editor.Msg) = toMsg >> Shell.Editor >> dispatch
  let excerpt: HTMLTextAreaElement option ref = ref None

  article (class' = (if isPreviewing (model ()) then "editor-column is-hidden" else "editor-column")) {
    textarea(
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
            (send Editor.BodyChanged))

      onCleanup (fun () -> view.destroy ()))
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
    topBar dispatch back model

    main (class' = "screen editor-screen") {
      Banner.view dispatch (model ()).Session
      h1 (class' = "sr-only") { heading () }

      Show(when' = isLoaded (model ()), fallback = loading model) { writing dispatch model }
      Show(when' = isPreviewing (model ())) { preview model }
    }

    DetailsView.sheet dispatch model
  }
