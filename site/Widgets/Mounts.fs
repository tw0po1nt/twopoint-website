[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.Mounts

open Browser
open Browser.Types

let private attributes (element: Element) =
  [ for index in 0 .. element.attributes.length - 1 do
      let attribute = element.attributes.item index
      attribute.name, attribute.value ]

let find (name: string) : (HTMLElement * Contract.Widget) list =
  let mounts = document.querySelectorAll $"[data-widget=\"{name}\"]"

  [ for index in 0 .. mounts.length - 1 do
      let mount = mounts.item index :?> HTMLElement

      match Contract.parse (attributes mount) with
      | Ok widget -> mount, widget
      | Error reason -> console.error $"{name}: {reason}" ]
