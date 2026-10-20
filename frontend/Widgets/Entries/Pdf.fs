module TwoPoint.Site.Widgets.Entries.Pdf

open TwoPoint.Site.Widgets

let private pdf widget =
  match widget with
  | Contract.Widget.Pdf props -> Some props
  | _ -> None

PdfJs.globalWorkerOptions.workerSrc <- PdfJs.workerUrl ()

for mount, props in Mounts.find "pdf" pdf do
  Mount.replace mount (fun () -> PdfView.view props)
