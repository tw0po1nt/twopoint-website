[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.PdfJs

open Browser.Types
open Fable.Core

type PageViewport =
  abstract width: float
  abstract height: float
  abstract scale: float
  abstract userUnit: float

type RenderTask =
  abstract promise: JS.Promise<unit>
  abstract cancel: unit -> unit

type PdfPage =
  abstract getViewport: {| scale: float |} -> PageViewport
  abstract render: {| canvas: HTMLCanvasElement; viewport: PageViewport |} -> RenderTask
  abstract streamTextContent: unit -> obj
  abstract getAnnotations: unit -> JS.Promise<obj array>

type PdfDocument =
  abstract numPages: int
  abstract getPage: pageNumber: int -> JS.Promise<PdfPage>
  /// The result is null when the PDF has no destination of that name.
  abstract getDestination: name: string -> JS.Promise<obj array>
  /// The index of the first page is 0.
  abstract getPageIndex: reference: obj -> JS.Promise<int>

type LoadingTask =
  abstract promise: JS.Promise<PdfDocument>

type WorkerOptions =
  abstract workerSrc: string with get, set

type TextLayer =
  abstract render: unit -> JS.Promise<unit>
  abstract cancel: unit -> unit

type AnnotationLayer =
  abstract render: {| annotations: obj array; div: HTMLElement; page: PdfPage; viewport: PageViewport; linkService: LinkService; renderForms: bool |} -> JS.Promise<unit>

/// The annotation layer calls these members when it makes a link and when the reader clicks the link.
and LinkService =
  abstract addLinkAttributes: link: HTMLAnchorElement * url: string * newWindow: bool -> unit
  abstract getDestinationHash: destination: obj -> string
  abstract getAnchorUrl: anchor: string -> string
  abstract goToDestination: destination: obj -> unit
  abstract executeNamedAction: action: string -> unit
  abstract executeSetOCGState: action: obj -> unit
  abstract getAttachmentContent: id: string -> JS.Promise<obj>

[<Import("GlobalWorkerOptions", "pdfjs-dist")>]
let globalWorkerOptions: WorkerOptions = jsNative

[<Import("getDocument", "pdfjs-dist")>]
let getDocument (source: {| url: string |}) : LoadingTask = jsNative

[<Import("TextLayer", "pdfjs-dist")>]
let private textLayerClass: obj = jsNative

[<Import("AnnotationLayer", "pdfjs-dist")>]
let private annotationLayerClass: obj = jsNative

[<Emit "new $0($1)">]
let private construct (constructor: obj) (options: obj) : 'T = jsNative

/// `textContentSource` is the result of `PdfPage.streamTextContent`.
let textLayer (options: {| textContentSource: obj; container: HTMLElement; viewport: PageViewport |}) : TextLayer =
  construct textLayerClass options

let annotationLayer (options: {| div: HTMLElement; page: PdfPage; viewport: PageViewport; linkService: LinkService |}) : AnnotationLayer =
  construct annotationLayerClass options

/// pdf.js gives these names to the error of a canceled render and of a canceled text layer.
let canceledNames = [ "RenderingCancelledException"; "AbortException" ]

/// The build replaces `PDF_WORKER_FILE` with the hashed worker file name, next to the widget files.
[<Emit "new URL(PDF_WORKER_FILE, import.meta.url).href">]
let workerUrl () : string = jsNative
