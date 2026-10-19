[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.Mount

open Browser.Types
open Oxpecker.Solid

let replace (mount: HTMLElement) (view: unit -> HtmlElement) =
  mount.textContent <- ""
  render (view, mount)
