[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.Mount

open Browser
open Browser.Types
open Oxpecker.Solid

let private attributes (element: Element) =
  [ for index in 0 .. element.attributes.length - 1 do
      let attribute = element.attributes.item index
      attribute.name, attribute.value ]

let each (name: string) (view: Contract.Widget -> HtmlElement) =
  let mounts = document.querySelectorAll $"[data-widget=\"{name}\"]"

  for index in 0 .. mounts.length - 1 do
    let mount = mounts.item index

    match Contract.parse (attributes mount) with
    | Ok widget ->
      mount.textContent <- ""
      render ((fun () -> view widget), mount)
    | Error reason -> console.error $"{name}: {reason}"
