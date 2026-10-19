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
    mutable Document: PdfJs.PdfDocument option
    mutable Task: PdfJs.RenderTask option
    /// The width in CSS pixels of the last draw.
    mutable Width: float option
    /// The page of the last `Draw`. A page that loads after a later `Draw` does not draw.
    mutable Wanted: int }

[<Emit "new ResizeObserver($0)">]
let private resizeObserver (callback: unit -> unit) : obj = jsNative

let private failed (dispatch: Pdf.Msg -> unit) (what: string) (error: exn) =
  if error?name <> PdfJs.canceledName then
    console.error ($"pdf: {what}", error)
    dispatch Pdf.Failed

let private drawOn (viewer: Viewer) (canvas: HTMLCanvasElement) (pageNumber: int) (page: PdfJs.PdfPage) =
  if viewer.Wanted <> pageNumber then
    Promise.lift ()
  else
    viewer.Task |> Option.iter _.cancel()
    let natural = page.getViewport {| scale = 1.0 |}
    let width = if canvas.clientWidth > 0.0 then canvas.clientWidth else natural.width
    viewer.Width <- Some width
    let viewport = page.getViewport {| scale = width * window.devicePixelRatio / natural.width |}
    canvas.width <- floor viewport.width
    canvas.height <- floor viewport.height
    let task = page.render {| canvas = canvas; viewport = viewport |}
    viewer.Task <- Some task
    task.promise

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

    match viewer.Document, viewer.Canvas with
    | Some document, Some canvas ->
      document.getPage pageNumber
      |> Promise.bind (drawOn viewer canvas pageNumber)
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
      Document = None
      Task = None
      Width = None
      Wanted = 0 }

  let model, dispatch = Mvu.run (Pdf.init props.File) Pdf.update (perform viewer)

  div (class' = "pdf-viewer") {
    canvas(class' = "pdf-page", role = "img", ariaLabel = countText (model ()))
      .bool("hidden", not (isShowing (model ())))
      .ref (fun element ->
        viewer.Canvas <- Some element
        watchWidth viewer dispatch element)

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
