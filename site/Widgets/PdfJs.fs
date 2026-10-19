/// The part of `pdfjs-dist` that the Pdf widget uses.
[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.PdfJs

open Browser.Types
open Fable.Core

type PageViewport =
  abstract width: float
  abstract height: float

type RenderTask =
  abstract promise: JS.Promise<unit>
  abstract cancel: unit -> unit

type PdfPage =
  abstract getViewport: {| scale: float |} -> PageViewport
  abstract render: {| canvas: HTMLCanvasElement; viewport: PageViewport |} -> RenderTask

type PdfDocument =
  abstract numPages: int
  abstract getPage: pageNumber: int -> JS.Promise<PdfPage>

type LoadingTask =
  abstract promise: JS.Promise<PdfDocument>

type WorkerOptions =
  abstract workerSrc: string with get, set

[<Import("GlobalWorkerOptions", "pdfjs-dist")>]
let globalWorkerOptions: WorkerOptions = jsNative

[<Import("getDocument", "pdfjs-dist")>]
let getDocument (source: {| url: string |}) : LoadingTask = jsNative

/// pdf.js gives this name to the error of a canceled render.
[<Literal>]
let canceledName = "RenderingCancelledException"

/// `build.mjs` replaces `PDF_WORKER_FILE` with the name of the hashed worker file, next to the widget files.
[<Emit "new URL(PDF_WORKER_FILE, import.meta.url).href">]
let workerUrl () : string = jsNative
