[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.ConfirmSubscription

open TwoPoint.Api
open TwoPoint.Api.Subscriptions

type Failure =
  | Refused
  | Unreachable

/// A mail scanner that opens the link runs no click, so only `ConfirmSelected` sends the confirm.
type Model =
  | MissingId
  | Ready of subscriptionId: string
  | Confirming of subscriptionId: string
  | ConfirmFailed of subscriptionId: string * Failure
  | Confirmed
  | Unknown

type Msg =
  | ConfirmSelected
  | Answered of ApiResponse<ConfirmState>
  | Unanswered

type Effect = Confirm of subscriptionId: string

/// The confirmation email sets this query key.
[<Literal>]
let private subscriptionIdKey = "subscriptionId"

let init (query: (string * string) list) : Model * Effect option =
  match query |> List.tryFind (fun (name, _) -> name = subscriptionIdKey) with
  | Some(_, id) when id.Trim() <> "" -> Ready(id.Trim()), None
  | _ -> MissingId, None

let update (msg: Msg) (model: Model) : Model * Effect option =
  match model, msg with
  | Ready id, ConfirmSelected
  | ConfirmFailed(id, _), ConfirmSelected -> Confirming id, Some(Confirm id)
  | Confirming id, Answered response ->
    match (if response.Success then response.Data else None) with
    | Some ConfirmState.Confirmed -> Confirmed, None
    | Some ConfirmState.Unknown -> Unknown, None
    | None -> ConfirmFailed(id, Refused), None
  | Confirming id, Unanswered -> ConfirmFailed(id, Unreachable), None
  | _ -> model, None
