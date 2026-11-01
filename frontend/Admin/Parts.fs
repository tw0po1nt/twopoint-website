[<RequireQualifiedAccess>]
module TwoPoint.Admin.Parts

open Browser
open Browser.Types
open Fable.Core.JsInterop
open Oxpecker.Solid
open Oxpecker.Solid.Aria
open System

type Tone =
  | Plain
  | Good
  | Warn
  | Bad
  | Info

let toneClass (tone: Tone) =
  match tone with
  | Plain -> ""
  | Good -> "ok"
  | Warn -> "warn"
  | Bad -> "bad"
  | Info -> "info"

type ButtonKind =
  | Secondary
  /// Ink on paper. One primary button on a screen at most.
  | Primary
  | Accent
  | Ghost
  /// A ghost button with the danger color for its text, for a Delete that opens a confirm dialog.
  | GhostDanger
  | Danger

type ButtonSize =
  | Small
  | Medium
  | Large
  /// A square button with only an icon. It needs a label.
  | IconOnly

let buttonClass (kind: ButtonKind) (size: ButtonSize) =
  let kind =
    match kind with
    | Secondary -> ""
    | Primary -> " primary"
    | Accent -> " accent"
    | Ghost -> " ghost"
    | GhostDanger -> " ghost bad"
    | Danger -> " danger"

  let size =
    match size with
    | Small -> " sm"
    | Medium -> ""
    | Large -> " lg"
    | IconOnly -> " icon-only"

  $"btn{kind}{size}"

type Button() =
  member val kind = Secondary with get, set
  member val size = Medium with get, set
  member val disabled = false with get, set
  /// The accessible name. Null when the content has text.
  member val label: string = null with get, set
  member val onClick: MouseEvent -> unit = ignore with get, set

  [<SolidComponent>]
  member this.Has(content: #HtmlElement) =
    button (
      type' = "button",
      class' = buttonClass this.kind this.size,
      disabled = this.disabled,
      ariaLabel = this.label,
      onClick = this.onClick
    ) {
      content
    }

type Input() =
  member val id: string = null with get, set
  member val kind = "text" with get, set
  member val value: unit -> string = (fun () -> "") with get, set
  member val placeholder: string = null with get, set
  member val disabled = false with get, set
  /// Read again at each change, so the input is disabled while this gives true.
  member val disabledWhen: unit -> bool = (fun () -> false) with get, set
  member val onInput: InputEvent -> unit = ignore with get, set
  member val onEnter: unit -> unit = ignore with get, set

  [<SolidComponent>]
  member this.View() =
    input(
      id = this.id,
      type' = this.kind,
      class' = "input",
      value = this.value (),
      placeholder = this.placeholder,
      disabled = (this.disabled || this.disabledWhen ()),
      onInput = this.onInput
    )
      .on ("keydown", fun event ->
        let key = event :?> KeyboardEvent

        if key.key = "Enter" then
          key.preventDefault ()
          this.onEnter ())

type Select() =
  member val id: string = null with get, set
  member val value: unit -> string = (fun () -> "") with get, set
  member val disabled = false with get, set
  member val onChange: Event -> unit = ignore with get, set

  [<SolidComponent>]
  member this.Has(options: #HtmlElement) =
    select(id = this.id, class' = "select", disabled = this.disabled, onChange = this.onChange)
      .ref(fun element -> createEffect (fun () -> element?value <- this.value ())) {
      options
    }

type Textarea() =
  member val id: string = null with get, set
  member val value: unit -> string = (fun () -> "") with get, set
  member val rows = 3 with get, set
  member val disabled = false with get, set
  member val onInput: InputEvent -> unit = ignore with get, set

  [<SolidComponent>]
  member this.View() =
    textarea(id = this.id, class' = "textarea", rows = this.rows, disabled = this.disabled, onInput = this.onInput)
      .ref (fun element -> createEffect (fun () -> element?value <- this.value ()))

type Field() =
  /// The id of the control.
  member val id: string = null with get, set
  member val label = "" with get, set
  member val hint: string = null with get, set
  member val error: string = null with get, set

  [<SolidComponent>]
  member this.Has(control: #HtmlElement) =
    div (class' = "field") {
      label (for' = this.id) { this.label }
      control
      Show(when' = not (String.IsNullOrEmpty this.hint)) { p (class' = "hint") { this.hint } }
      Show(when' = not (String.IsNullOrEmpty this.error)) { p (class' = "err", role = "alert") { this.error } }
    }

type Search() =
  member val label = "Search" with get, set
  member val value: unit -> string = (fun () -> "") with get, set
  member val placeholder: string = null with get, set
  member val onInput: InputEvent -> unit = ignore with get, set

  [<SolidComponent>]
  member this.View() =
    div (class' = "search", role = "search") {
      Icon.view Icon.Search

      input (
        type' = "search",
        class' = "input",
        ariaLabel = this.label,
        value = this.value (),
        placeholder = this.placeholder,
        onInput = this.onInput
      )
    }

[<SolidComponent>]
let badge (tone: Tone) (text: string) =
  span (class' = $"badge {toneClass tone}") { text }

[<SolidComponent>]
let count (value: int) = span (class' = "badge count") { string value }

/// The sign in front of the text of a state mark.
type Sign =
  | Dot
  | Pulse
  | Spin
  | Warning

[<SolidComponent>]
let stateMark (tone: Tone) (sign: Sign) (text: string) =
  span (class' = $"state-mark {toneClass tone}") {
    match sign with
    | Dot -> span (class' = "dot")
    | Pulse -> span (class' = "dot pulse")
    | Spin -> span (class' = "spinner")
    | Warning -> span (class' = "sign") { Icon.view Icon.Alert }

    text
  }

[<SolidComponent>]
let card (content: #HtmlElement) = div (class' = "card") { content }

[<SolidComponent>]
let cardHead (content: #HtmlElement) = div (class' = "card-head") { content }

[<SolidComponent>]
let cardBody (content: #HtmlElement) = div (class' = "card-body") { content }

type Alert() =
  member val tone = Info with get, set
  member val title: string = null with get, set
  /// More classes for the frame of the alert.
  member val extra = "" with get, set

  [<SolidComponent>]
  member this.Has(content: #HtmlElement) =
    div (class' = $"alert {toneClass this.tone} {this.extra}", role = "alert") {
      Icon.view Icon.Alert

      div (class' = "alert-body") {
        Show(when' = not (String.IsNullOrEmpty this.title)) { p (class' = "alert-title") { this.title } }
        content
      }
    }

/// How a column shows at a phone width, where each row becomes two lines.
type Column =
  /// The first line.
  | Main
  /// Joined on the second line with "·".
  | Meta
  /// At the right of the two lines, for a status or an action.
  | End
  /// Only on a wide screen.
  | Wide

let columnClass (column: Column) =
  match column with
  | Main -> "c-main"
  | Meta -> "c-meta"
  | End -> "c-end"
  | Wide -> "c-wide"

[<SolidComponent>]
let dataTable (label: string) (head: #HtmlElement) (rows: #HtmlElement) =
  div (class' = "table-frame responsive") {
    table (class' = "table responsive", ariaLabel = label) {
      thead () { tr () { head } }
      tbody () { rows }
    }
  }

[<SolidComponent>]
let headCell (column: Column) (text: string) = th (class' = columnClass column) { text }

[<SolidComponent>]
let cell (column: Column) (content: #HtmlElement) = td (class' = columnClass column) { content }

[<SolidComponent>]
let tabs (label: string) (options: ('T * string) list) (selected: unit -> 'T) (choose: 'T -> unit) =
  div (class' = "tabs", role = "tablist", ariaLabel = label) {
    For(each = Array.ofList options) {
      yield
        fun (value, text) _ ->
          button (type' = "button", role = "tab", ariaSelected = (selected () = value), onClick = fun _ -> choose value) {
            text
          }
    }
  }

let initials (name: string) =
  let first (word: string) = word.Substring(0, 1).ToUpperInvariant()

  match name.Split([| ' ' |], StringSplitOptions.RemoveEmptyEntries) with
  | [||] -> ""
  | [| word |] -> first word
  | words -> first words[0] + first words[words.Length - 1]

[<SolidComponent>]
let avatar (name: string) =
  span (class' = "avatar", ariaHidden = true) { initials name }

[<SolidComponent>]
let kbd' (text: string) = kbd () { text }

[<SolidComponent>]
let switch' (id: string) (label: string) (isOn: unit -> bool) (toggle: unit -> unit) =
  button (
    id = id,
    type' = "button",
    class' = "switch",
    role = "switch",
    ariaLabel = label,
    ariaChecked = (if isOn () then "true" else "false"),
    onClick = fun _ -> toggle ()
  )

let focusable (menu: Element) =
  menu.querySelectorAll "[role='menuitem']:not([aria-disabled='true'])"
  |> fun items -> [ for index in 0 .. int items.length - 1 -> items.item index :?> HTMLElement ]

let placeMenu (placement: Menu.Placement) (menu: HTMLElement) =
  match document.querySelector $"[popovertarget='{menu.id}']" with
  | null -> ()
  | trigger ->
    let box = trigger.getBoundingClientRect ()

    let position =
      Menu.place
        placement
        { Left = box.left
          Top = box.top
          Right = box.right
          Bottom = box.bottom }
        { Width = window.innerWidth
          Height = window.innerHeight }

    menu.setAttribute ("style", Menu.style position)

let focusIn (menu: Element) (move: Menu.Move) =
  let items = focusable menu
  let current = items |> List.tryFindIndex (fun item -> item = (document.activeElement :?> HTMLElement))

  Menu.target move items.Length current
  |> Option.iter (fun index -> items[index].focus ())

/// The browser opens the menu from a button with `popovertarget` set to the id of the menu, and closes it at Esc or
/// at a click outside.
[<SolidComponent>]
let menu (id: string) (placement: Menu.Placement) (items: #HtmlElement) =
  div(id = id, class' = "menu", role = "menu", popover = "auto")
    .on("beforetoggle", fun event ->
      if event?newState = "open" then
        placeMenu placement (event.currentTarget :?> HTMLElement))
    .on("toggle", fun event ->
      if event?newState = "open" then
        focusIn (event.currentTarget :?> Element) Menu.First)
    .on (
      "keydown",
      fun event ->
        match Menu.move (event :?> KeyboardEvent).key with
        | Some move ->
          event.preventDefault ()
          focusIn (event.currentTarget :?> Element) move
        | None -> ()
    ) {
    items
  }

[<SolidComponent>]
let menuItem (menuId: string) (tone: Tone) (choose: unit -> unit) (content: #HtmlElement) =
  button (
    type' = "button",
    class' = $"item {toneClass tone}",
    role = "menuitem",
    tabindex = -1,
    popovertarget = menuId,
    popovertargetaction = "hide",
    onClick = fun _ -> choose ()
  ) {
    content
  }

[<SolidComponent>]
let menuLink (url: string) (content: #HtmlElement) =
  a (class' = "item", role = "menuitem", tabindex = -1, href = url, target = "_blank", rel = "noopener") { content }

[<SolidComponent>]
let menuUnavailable (reason: string) (content: #HtmlElement) =
  div (class' = "item", role = "menuitem", ariaDisabled = true) {
    content
    small () { reason }
  }

[<SolidComponent>]
let menuLabel (text: string) = div (class' = "label") { text }

[<SolidComponent>]
let menuSeparator () = div (class' = "sep", role = "separator")

/// `showModal` gives the focus trap, the Esc key, the top layer, and an inert page.
let syncDialog (isOpen: bool) (element: HTMLElement) =
  match isOpen, element?``open`` with
  | true, false -> element?showModal ()
  | false, true -> element?close ()
  | _ -> ()

let followOpen (isOpen: unit -> bool) (element: HTMLElement) =
  createEffect (fun () -> syncDialog (isOpen ()) element)

/// `closed` runs after each close, also a close from the browser at the Esc key.
[<SolidComponent>]
let modalDialog (id: string) (isOpen: unit -> bool) (closed: unit -> unit) (title: string) (body: #HtmlElement) (actions: #HtmlElement) =
  dialog(id = id, class' = "dialog", ariaLabelledBy = $"{id}-title")
    .ref(followOpen isOpen)
    .on ("close", fun _ -> closed ()) {
    h2 (id = $"{id}-title") { title }
    div (class' = "dialog-body") { body }
    div (class' = "actions") { actions }
  }

/// A panel at the right on a wide screen, and from the bottom on a phone. A click outside the panel closes it.
[<SolidComponent>]
let sheet (id: string) (isOpen: unit -> bool) (closed: unit -> unit) (label: string) (content: #HtmlElement) =
  dialog(
    id = id,
    class' = "sheet",
    ariaLabel = label,
    onClick =
      fun event ->
        if event.target = event.currentTarget then
          event.currentTarget?close ()
  )
    .ref(followOpen isOpen)
    .on ("close", fun _ -> closed ()) {
    div (class' = "sheet-frame") { content }
  }

[<SolidComponent>]
let sheetHead (title: string) (close: unit -> unit) =
  div (class' = "sheet-head") {
    h2 (class' = "grow ellipsis") { title }
    Button(kind = Ghost, size = IconOnly, label = "Close", onClick = fun _ -> close ()).Has(Icon.view Icon.Close)
  }

[<SolidComponent>]
let sheetBody (content: #HtmlElement) = div (class' = "sheet-body") { content }

[<SolidComponent>]
let sheetFoot (content: #HtmlElement) = div (class' = "sheet-foot") { content }

[<SolidComponent>]
let toasts (content: #HtmlElement) =
  div (class' = "toasts", role = "status", ariaLive = "polite") { content }

[<SolidComponent>]
let toast (content: #HtmlElement) = div (class' = "toast") { content }

/// `width` is a CSS length.
[<SolidComponent>]
let skeleton (width: string) =
  div (class' = "skeleton", style = $"width: {width}", ariaHidden = true)

[<SolidComponent>]
let spinner () = span (class' = "spinner", ariaHidden = true)

[<SolidComponent>]
let emptyState (icon: Icon.Icon) (title: string) (content: #HtmlElement) =
  div (class' = "empty") {
    span (class' = "icon-wrap") { Icon.view icon }
    h2 () { title }
    content
  }
