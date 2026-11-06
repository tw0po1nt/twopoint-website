[<RequireQualifiedAccess>]
module TwoPoint.Admin.ChangeDate

let initial (publishDate: string option) =
  match publishDate with
  | Some date when date.Length >= 16 -> date.Substring(0, 16)
  | _ -> ""

let iso (state: Typed.State) = Publish.isoOf (Typed.typed state)

let canConfirm (state: Typed.State) =
  match state with
  | Typed.Typing _
  | Typed.Failed _ -> (iso state).IsSome
  | Typed.Closed
  | Typed.Sending _ -> false

let statusText (state: Typed.State) =
  match state with
  | Typed.Closed -> ""
  | Typed.Typing typed
  | Typed.Failed(typed, _) when (Publish.isoOf typed).IsNone -> "Enter a date and time."
  | Typed.Typing _ -> "A past date publishes the post right away."
  | Typed.Sending _ -> "Changing the date…"
  | Typed.Failed _ -> "Couldn't change the date."
