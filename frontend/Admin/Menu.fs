[<RequireQualifiedAccess>]
module TwoPoint.Admin.Menu

/// The menu is in the top layer, so its position is relative to the viewport.
type Box =
  { Left: float
    Top: float
    Right: float
    Bottom: float }

type Viewport = { Width: float; Height: float }

type Side =
  | Above
  | Below

/// The edge of the trigger where the menu lines up.
type Edge =
  | Start
  | End

type Placement = { Side: Side; Edge: Edge }

type Vertical =
  | Top of float
  | Bottom of float

type Horizontal =
  | Left of float
  | Right of float

type Position =
  { Vertical: Vertical
    Horizontal: Horizontal }

let gap = 6.

let place (placement: Placement) (trigger: Box) (viewport: Viewport) =
  { Vertical =
      match placement.Side with
      | Above -> Bottom(viewport.Height - trigger.Top + gap)
      | Below -> Top(trigger.Bottom + gap)
    Horizontal =
      match placement.Edge with
      | Start -> Left trigger.Left
      | End -> Right(viewport.Width - trigger.Right) }

let style (position: Position) =
  let vertical =
    match position.Vertical with
    | Top value -> $"top: {value}px"
    | Bottom value -> $"bottom: {value}px"

  let horizontal =
    match position.Horizontal with
    | Left value -> $"left: {value}px"
    | Right value -> $"right: {value}px"

  $"{vertical}; {horizontal}"

type Move =
  | Next
  | Previous
  | First
  | Last

let move (key: string) =
  match key with
  | "ArrowDown" -> Some Next
  | "ArrowUp" -> Some Previous
  | "Home" -> Some First
  | "End" -> Some Last
  | _ -> None

/// `current` is None when the focus is on no item of the menu.
let target (move: Move) (count: int) (current: int option) =
  if count = 0 then
    None
  else
    match move, current with
    | Next, Some index -> Some((index + 1) % count)
    | Previous, Some index -> Some((index - 1 + count) % count)
    | (Next | First), None
    | First, Some _ -> Some 0
    | (Previous | Last), None
    | Last, Some _ -> Some(count - 1)