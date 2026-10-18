[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.Head

open Fable.Core

// Each type and function here must compile to plain JavaScript, because the head script contains no `fable-library` code.

[<StringEnum; RequireQualifiedAccess>]
type Theme =
  | Light
  | Dark

let storageKey = "theme"

let name (theme: Theme) =
  match theme with
  | Theme.Light -> "light"
  | Theme.Dark -> "dark"

/// `value` can be `null`.
let parse (value: string) : Theme option =
  match value with
  | "light" -> Some Theme.Light
  | "dark" -> Some Theme.Dark
  | _ -> None

/// `forced` is the theme of `data-theme`. `None` follows the system setting.
let toggle (system: Theme) (forced: Theme option) : Theme option =
  let shown =
    match forced with
    | Some theme -> theme
    | None -> system

  let next =
    match shown with
    | Theme.Light -> Theme.Dark
    | Theme.Dark -> Theme.Light

  if next = system then None else Some next

/// A storage call that throws gives `None`.
let read (getItem: string -> string) : Theme option =
  try
    parse (getItem storageKey)
  with _ ->
    None

/// A storage call that throws stores nothing.
let write (setItem: string -> string -> unit) (removeItem: string -> unit) (forced: Theme option) =
  try
    match forced with
    | Some theme -> setItem storageKey (name theme)
    | None -> removeItem storageKey
  with _ ->
    ()
