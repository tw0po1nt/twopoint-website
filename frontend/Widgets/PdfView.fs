[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.PdfView

open Browser
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop
open Oxpecker.Solid
open Oxpecker.Solid.Aria

type private Viewer =
  { mutable Canvas: HTMLCanvasElement option
    mutable TextDiv: HTMLElement option
    mutable LinkDiv: HTMLElement option
    mutable Document: PdfJs.PdfDocument option
    mutable Task: PdfJs.RenderTask option
    mutable TextTask: PdfJs.TextLayer option
    /// The width in CSS pixels of the last draw.
    mutable Width: float option
    /// The page of the last `Draw`. A page that loads after a later `Draw` does not draw.
    mutable Wanted: int
    /// The count of draws. The links of a draw go on the page only while that draw is the last draw.
    mutable Draws: int }

type private Surface =
  { Canvas: HTMLCanvasElement
    TextDiv: HTMLElement
    LinkDiv: HTMLElement }

[<Emit "new ResizeObserver($0)">]
let private resizeObserver (callback: unit -> unit) : obj = jsNative

let private failed (dispatch: Pdf.Msg -> unit) (what: string) (error: exn) =
  if not (List.contains error?name PdfJs.canceledNames) then
    console.error ($"pdf: {what}", error)
    dispatch Pdf.Failed

/// The first item of an explicit destination is a page reference or a page index.
let private pageIndex (document: PdfJs.PdfDocument) (explicit: obj array) : JS.Promise<int option> =
  if isNullOrUndefined explicit || Array.isEmpty explicit then
    Promise.lift None
  elif jsTypeof explicit[0] = "number" then
    Promise.lift (Some(unbox<int> explicit[0]))
  else
    document.getPageIndex explicit[0] |> Promise.map Some

let private goTo (viewer: Viewer) (dispatch: Pdf.Msg -> unit) (destination: obj) =
  match viewer.Document with
  | Some document ->
    match destination with
    | :? string as name -> document.getDestination name
    | _ -> Promise.lift (unbox<obj array> destination)
    |> Promise.bind (pageIndex document)
    |> Promise.map (Option.iter (fun index -> dispatch (Pdf.GoTo(index + 1))))
    |> Promise.catch (fun error -> console.error ("pdf: the link has no page.", error))
    |> Promise.start
  | None -> ()

let private linkService (viewer: Viewer) (dispatch: Pdf.Msg -> unit) =
  { new PdfJs.LinkService with
      member _.addLinkAttributes(link, url, newWindow) =
        link.href <- url
        link.title <- url
        link.rel <- "noopener noreferrer nofollow"

        if newWindow then
          link.target <- "_blank"

      member _.getDestinationHash _ = "#"
      member _.getAnchorUrl _ = "#"
      member _.goToDestination destination = goTo viewer dispatch destination
      member _.executeNamedAction _ = ()
      member _.executeSetOCGState _ = ()
      member _.getAttachmentContent _ = Promise.lift null }

let private drawLinks (viewer: Viewer) (links: PdfJs.LinkService) (div: HTMLElement) (draw: int) (page: PdfJs.PdfPage) (viewport: PdfJs.PageViewport) =
  page.getAnnotations ()
  |> Promise.bind (fun annotations ->
    if viewer.Draws <> draw then
      Promise.lift ()
    else
      let layer =
        PdfJs.annotationLayer
          {| div = div
             page = page
             viewport = viewport
             linkService = links |}

      layer.render
        {| annotations = annotations
           div = div
           page = page
           viewport = viewport
           linkService = links
           renderForms = false |})

/// The canvas gets a bitmap of the device pixels. The text and the links use CSS pixels.
let private drawOn (viewer: Viewer) (links: PdfJs.LinkService) (surface: Surface) (pageNumber: int) (page: PdfJs.PdfPage) =
  if viewer.Wanted <> pageNumber then
    Promise.lift ()
  else
    viewer.Task |> Option.iter _.cancel()
    viewer.TextTask |> Option.iter _.cancel()
    viewer.Draws <- viewer.Draws + 1
    let canvas = surface.Canvas
    let natural = page.getViewport {| scale = 1.0 |}
    let width = if canvas.clientWidth > 0.0 then canvas.clientWidth else natural.width
    viewer.Width <- Some width
    let viewport = page.getViewport {| scale = width / natural.width |}
    let bitmap = page.getViewport {| scale = width * window.devicePixelRatio / natural.width |}
    canvas.width <- floor bitmap.width
    canvas.height <- floor bitmap.height
    canvas.parentElement?style?setProperty ("--total-scale-factor", string (viewport.scale * viewport.userUnit))
    surface.TextDiv.textContent <- ""
    surface.LinkDiv.textContent <- ""
    let task = page.render {| canvas = canvas; viewport = bitmap |}
    viewer.Task <- Some task

    let text =
      PdfJs.textLayer
        {| textContentSource = page.streamTextContent ()
           container = surface.TextDiv
           viewport = viewport |}

    viewer.TextTask <- Some text

    Promise.all [ task.promise; text.render (); drawLinks viewer links surface.LinkDiv viewer.Draws page viewport ]
    |> Promise.map ignore

let private perform (viewer: Viewer) (effect: Pdf.Effect) (dispatch: Pdf.Msg -> unit) =
  match effect with
  | Pdf.Open file ->
    PdfJs.getDocument({| url = file |}).promise
    |> Promise.map (fun document ->
      viewer.Document <- Some document
      dispatch (Pdf.Opened document.numPages))
    |> Promise.catch (failed dispatch "the PDF did not load.")
    |> Promise.start
  | Pdf.Draw pageNumber ->
    viewer.Wanted <- pageNumber

    match viewer.Document, viewer.Canvas, viewer.TextDiv, viewer.LinkDiv with
    | Some document, Some canvas, Some textDiv, Some linkDiv ->
      let surface =
        { Canvas = canvas
          TextDiv = textDiv
          LinkDiv = linkDiv }

      document.getPage pageNumber
      |> Promise.bind (drawOn viewer (linkService viewer dispatch) surface pageNumber)
      |> Promise.catch (failed dispatch "the page did not draw.")
      |> Promise.start
    | _ -> ()

/// A change of width draws the page again, so that the bitmap stays sharp. A change of height does not.
let private watchWidth (viewer: Viewer) (dispatch: Pdf.Msg -> unit) (canvas: HTMLCanvasElement) =
  let observer =
    resizeObserver (fun () ->
      match viewer.Width with
      | Some width when canvas.clientWidth > 0.0 && canvas.clientWidth <> width -> dispatch Pdf.Resized
      | _ -> ())

  observer?observe canvas
  onCleanup (fun () -> observer?disconnect ())

let private countText (model: Pdf.Model) =
  match model with
  | Pdf.Showing page -> $"page {page.Number} of {page.Count}"
  | Pdf.Opening
  | Pdf.Broken -> ""

let private statusText (model: Pdf.Model) =
  match model with
  | Pdf.Opening -> "The site loads the PDF."
  | Pdf.Broken -> "The PDF did not load. Use the download link."
  | Pdf.Showing _ -> ""

let private statusClass (model: Pdf.Model) =
  match model with
  | Pdf.Broken -> "pdf-status is-failed"
  | Pdf.Opening
  | Pdf.Showing _ -> "pdf-status"

let private isShowing (model: Pdf.Model) =
  match model with
  | Pdf.Showing _ -> true
  | Pdf.Opening
  | Pdf.Broken -> false

[<SolidComponent>]
let view (props: Contract.PdfProps) =
  let viewer =
    { Canvas = None
      TextDiv = None
      LinkDiv = None
      Document = None
      Task = None
      TextTask = None
      Width = None
      Wanted = 0
      Draws = 0 }

  let model, dispatch = Mvu.run (Pdf.init props.File) Pdf.update (perform viewer)

  div (class' = "pdf-viewer") {
    div(class' = "pdf-frame").bool ("hidden", not (isShowing (model ()))) {
      canvas(class' = "pdf-page", role = "img", ariaLabel = countText (model ()))
        .ref (fun element ->
          viewer.Canvas <- Some element
          watchWidth viewer dispatch element)

      div(class' = "textLayer").ref (fun element -> viewer.TextDiv <- Some element)
      div(class' = "annotationLayer").ref (fun element -> viewer.LinkDiv <- Some element)
    }

    Show(when' = isShowing (model ())) {
      nav (class' = "pdf-nav", ariaLabel = "PDF pages") {
        button (type' = "button", disabled = not (Pdf.hasPrevious (model ())), onClick = fun _ -> dispatch Pdf.Previous) {
          "[previous]"
        }

        span(class' = "pdf-count").attr ("aria-live", "polite") { countText (model ()) }

        button (type' = "button", disabled = not (Pdf.hasNext (model ())), onClick = fun _ -> dispatch Pdf.Next) { "[next]" }
      }
    }

    Show(when' = not (isShowing (model ()))) {
      p(class' = statusClass (model ())).attr ("aria-live", "polite") { statusText (model ()) }
    }
  }
