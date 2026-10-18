module TwoPoint.Site.Widgets.HeadMain

open Browser
open Browser.Types
open Fable.Core

let private root = document.documentElement

let private apply (forced: Head.Theme option) =
  match forced with
  | Some theme -> root.setAttribute ("data-theme", Head.name theme)
  | None -> root.removeAttribute "data-theme"

let private system () =
  if window.matchMedia("(prefers-color-scheme: dark)").matches then
    Head.Theme.Dark
  else
    Head.Theme.Light

apply (Head.read (fun key -> localStorage.getItem key))

document.addEventListener (
  "click",
  fun event ->
    let target = event.target :?> Element

    if (target.closest "[data-theme-toggle]").IsSome then
      let forced = Head.toggle (system ()) (Head.parse (root.getAttribute "data-theme"))
      apply forced
      Head.write (fun key value -> localStorage.setItem (key, value)) (fun key -> localStorage.removeItem key) forced
)

[<Emit "new Date().toISOString()">]
let private now () : string = jsNative

let private setHiddenAfter (attribute: string) (hidden: bool) (now: string) =
  let elements = document.querySelectorAll ("[" + attribute + "]")

  for index in 0 .. elements.length - 1 do
    let element = elements.item index :?> HTMLElement

    if HideAt.isPast now (element.getAttribute attribute) then
      element.hidden <- hidden

let private applyPublishDates () =
  let now = now ()
  setHiddenAfter HideAt.attribute true now
  setHiddenAfter HideAt.showAttribute false now

// The script runs in the head, before the parser gets to the body.
document.addEventListener ("DOMContentLoaded", fun _ -> applyPublishDates ())
