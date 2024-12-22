module Feliz.QRCodeSVG

open Feliz
open Fable.Core.JsInterop

let qrCodeSvg: obj = import "QRCodeSVG" "qrcode.react"

type QRCodeSVG =
  static member inline Value (str: string) = "value" ==> str
  static member inline create props = Interop.reactApi.createElement (qrCodeSvg, createObj !! props)
