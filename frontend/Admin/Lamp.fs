[<RequireQualifiedAccess>]
module TwoPoint.Admin.Lamp

open Browser
open Browser.Types
open Fable.Core

type Point = { X: float; Y: float }

[<Emit "typeof document.startViewTransition === 'function'">]
let private hasViewTransitions () : bool = jsNative

[<Emit "window.matchMedia('(prefers-reduced-motion: reduce)').matches">]
let private reducesMotion () : bool = jsNative

[<Emit "$0.hasUAVisualTransition === true">]
let private animatedByBrowser (event: Event) : bool = jsNative

[<Emit "((transition, finished) => { transition.ready.catch(() => {}); transition.finished.then(finished, finished) })(document.startViewTransition($0), $1)">]
let private startViewTransition (update: unit -> unit) (finished: unit -> unit) : unit = jsNative

[<Emit "performance.now()">]
let private now () : float = jsNative

[<Emit "CSS.escape($0)">]
let private cssEscape (text: string) : string = jsNative

[<Emit "$0.style.setProperty($1, $2)">]
let private setStyle (element: Element) (property: string) (value: string) : unit = jsNative

[<Emit "$0.style.removeProperty($1)">]
let private removeStyle (element: Element) (property: string) : unit = jsNative

let motion () = if reducesMotion () then Transition.Reduced else Transition.Full

let support () = if hasViewTransitions () then Transition.ViewTransitions else Transition.NoViewTransitions

let back (event: Event) = if animatedByBrowser event then Transition.BrowserAnimated else Transition.AppAnimates

/// Gives the point of the last tap or click, for 1.5 s after it.
let taps () =
  let last = ref None

  document.addEventListener (
    "pointerdown",
    (fun (event: Event) ->
      let event = event :?> MouseEvent
      last.Value <- Some({ X = event.clientX; Y = event.clientY }, now ())),
    true
  )

  fun () -> last.Value |> Option.filter (fun (_, time) -> now () - time < 1500.) |> Option.map fst

let private root () = document.documentElement

let private screenCenter () = { X = window.innerWidth / 2.; Y = window.innerHeight / 2. }

let private centerOf (element: Element) =
  let box = element.getBoundingClientRect ()

  if box.width > 0. && box.height > 0. && box.bottom > 0. && box.top < window.innerHeight then
    Some { X = box.left + box.width / 2.; Y = box.top + box.height / 2. }
  else
    None

let private find (selector: string) = document.querySelector selector |> Option.ofObj

let private postOf (slug: string) =
  let row =
    document.getElementById slug
    |> Option.ofObj
    |> Option.filter (fun row -> row.classList.contains "queue-row")
    |> Option.map (fun row -> row :> Element)

  let link = find $"a[href=\"{cssEscape (Route.path (Post slug))}\"]"
  row |> Option.orElse link |> Option.bind centerOf

let private origin (tap: unit -> Point option) =
  let focused = document.activeElement |> Option.ofObj |> Option.filter (fun element -> element <> document.body)
  tap () |> Option.orElse (focused |> Option.bind centerOf) |> Option.defaultWith screenCenter

let private aim (point: Point) =
  let farthest = sqrt ((max point.X (window.innerWidth - point.X)) ** 2. + (max point.Y (window.innerHeight - point.Y)) ** 2.)
  setStyle (root ()) "--lamp-x" $"{point.X}px"
  setStyle (root ()) "--lamp-y" $"{point.Y}px"
  setStyle (root ()) "--lamp-end" $"{ceil (farthest + 180.)}px"

let private name (transitionName: string) (element: Element option) =
  element |> Option.iter (fun element -> setStyle element "view-transition-name" transitionName)

let private nameText () = name "paper-text" (find ".editor-screen")

let private addGlow () =
  let glow = document.createElement "div"
  glow.className <- "lamp-glow"
  glow.setAttribute ("aria-hidden", "true")
  document.body.appendChild glow |> ignore
  name "lamp-glow" (Some glow)

let private each (selector: string) (action: Element -> unit) =
  let found = document.querySelectorAll selector

  for i in found.length - 1 .. -1 .. 0 do
    action found.[i]

let private clear () =
  root().removeAttribute "data-transition"
  each ".lamp-glow" (fun glow -> glow.remove ())
  each "[style*='view-transition-name']" (fun element -> removeStyle element "view-transition-name")

let mutable private generation = 0

let private start (kind: string) =
  generation <- generation + 1
  let mine = generation
  clear ()
  root().setAttribute ("data-transition", kind)
  fun () -> if generation = mine then clear ()

/// Runs the commit of a new route inside the move.
let run (tap: unit -> Point option) (move: Transition.Move) (commit: unit -> unit) =
  match move with
  | Transition.NoMove -> commit ()
  | Transition.CrossFade ->
    let finish = start "fade"
    startViewTransition commit finish
  | Transition.Lamp Transition.IntoEditor ->
    let finish = start "lamp-in"
    aim (origin tap)
    addGlow ()

    startViewTransition
      (fun () ->
        commit ()
        nameText ())
      finish
  | Transition.Lamp(Transition.OutOfEditor slug) ->
    let finish = start "lamp-out"
    let posts = find ".editor-bar .back" |> Option.bind centerOf
    nameText ()
    addGlow ()

    startViewTransition
      (fun () ->
        commit ()
        aim (postOf slug |> Option.orElse posts |> Option.defaultWith screenCenter))
      finish
  | Transition.NewScreenOnly Transition.IntoEditor ->
    let finish = start "alone-in"
    aim (origin tap)
    addGlow ()
    commit ()
    window.setTimeout (finish, 900) |> ignore
  | Transition.NewScreenOnly(Transition.OutOfEditor _) ->
    let finish = start "alone-out"
    commit ()
    window.setTimeout (finish, 400) |> ignore
