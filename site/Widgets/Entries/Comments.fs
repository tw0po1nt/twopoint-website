module TwoPoint.Site.Widgets.Entries.Comments

open TwoPoint.Site.Widgets

open Browser
open Browser.Types
open Fable.Core
open Fable.Core.JsInterop

[<Emit "new URLSearchParams(location.search).has($0)">]
let private queryHas (key: string) : bool = jsNative

let private load (mount: HTMLElement) (props: Contract.CommentsProps) =
  importValueDynamic CommentsView.start
  |> Promise.map (fun start -> start {| Mount = mount; Props = props |})
  |> Promise.catch (fun error -> console.error ("comments: the view did not load.", error))
  |> Promise.start

let private watch (mount: HTMLElement) (props: Contract.CommentsProps) =
  match mount.closest "details" with
  | Some panel ->
    let mutable loaded = false

    let loadOnce () =
      if not loaded && panel?``open`` then
        loaded <- true
        load mount props

    if queryHas Comments.validationIdKey then
      panel?``open`` <- true

    loadOnce ()
    panel.addEventListener ("toggle", fun _ -> loadOnce ())
  | None -> load mount props

for mount, widget in Mounts.find "comments" do
  match widget with
  | Contract.Widget.Comments props -> watch mount props
  | Contract.Widget.Subscribe _ -> ()
