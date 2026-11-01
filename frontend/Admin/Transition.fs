[<RequireQualifiedAccess>]
module TwoPoint.Admin.Transition

type Direction =
  | IntoEditor
  | OutOfEditor of slug: string

type Motion =
  | Full
  | Reduced

type Support =
  | ViewTransitions
  | NoViewTransitions

type Back =
  | AppAnimates
  /// The browser animated the back itself, for example with the iOS edge swipe.
  | BrowserAnimated

type Move =
  | Lamp of Direction
  | CrossFade
  | NewScreenOnly of Direction
  | NoMove

let direction (from: Route) (target: Route) =
  match from, target with
  | Post _, Post _ -> None
  | _, Post _ -> Some IntoEditor
  | Post slug, _ -> Some(OutOfEditor slug)
  | _ -> None

let choose (motion: Motion) (support: Support) (back: Back) (from: Route) (target: Route) =
  match direction from target, back, support, motion with
  | None, _, _, _
  | Some _, BrowserAnimated, _, _
  | Some _, AppAnimates, NoViewTransitions, Reduced -> NoMove
  | Some _, AppAnimates, ViewTransitions, Reduced -> CrossFade
  | Some direction, AppAnimates, ViewTransitions, Full -> Lamp direction
  | Some direction, AppAnimates, NoViewTransitions, Full -> NewScreenOnly direction
