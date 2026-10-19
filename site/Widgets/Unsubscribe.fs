[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.Unsubscribe

open TwoPoint.Api
open TwoPoint.Api.Subscriptions

type Model =
  | Sending
  | NoSubscriptionId
  | Unsubscribed
  | Refused of reason: string
  | Unreachable

type Msg =
  | Answered of ApiResponse<unit>
  | Unanswered

type Effect = Cancel of SubscriptionCancellation

/// The backend sets this query key.
[<Literal>]
let private subscriptionIdKey = "subscriptionId"

let init (query: (string * string) list) : Model * Effect option =
  match query |> List.tryFind (fun (name, _) -> name = subscriptionIdKey) with
  | Some(_, id) when id.Trim() <> "" -> Sending, Some(Cancel { SubscriptionId = id.Trim() })
  | _ -> NoSubscriptionId, None

let private refused = "The unsubscribe request did not go through. Try again later."

let update (msg: Msg) (model: Model) : Model * Effect option =
  match model, msg with
  | Sending, Answered response when response.Success -> Unsubscribed, None
  | Sending, Answered response -> Refused(response.Message |> Option.defaultValue refused), None
  | Sending, Unanswered -> Unreachable, None
  | _ -> model, None
