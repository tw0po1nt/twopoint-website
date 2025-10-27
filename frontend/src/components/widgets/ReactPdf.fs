module Feliz.ReactPdf

open Browser.Types
open Fable.Core.JsInterop
open Feliz

open System

let private document: obj = import "Document" "react-pdf"
let private page: obj = import "Page" "react-pdf"
let private pdfjs: obj = import "pdfjs" "react-pdf"

importSideEffects "react-pdf/dist/Page/TextLayer.css"
importSideEffects "react-pdf/dist/Page/AnnotationLayer.css"

pdfjs?GlobalWorkerOptions?workerSrc <- "/pdf.worker.js"

type Page =
  static member inline PageNumber (pageNumber: int) = "pageNumber" ==> pageNumber
  static member inline create props = Interop.reactApi.createElement (page, createObj !! props)

type ReactPdf =
  static member inline File (file: string) = Interop.mkAttr "file" file
  static member inline OnLoadSuccess (fn: int -> unit) = 
    Interop.mkAttr "onLoadSuccess" (fun (ev: Event) ->
      let value : int = !!ev?numPages
      if not (isNullOrUndefined value) then
          fn value
    )

  static member inline document (props : IReactProperty list) = 
    Interop.reactApi.createElement (document, createObj !! props)

  static member inline PageNumber (pageNumber : int) = Interop.mkAttr "pageNumber" pageNumber

  static member inline Width (width : int) = Interop.mkAttr "width" width

  static member inline page (props : IReactProperty list) =
    Interop.reactApi.createElement (page, createObj !! props)


