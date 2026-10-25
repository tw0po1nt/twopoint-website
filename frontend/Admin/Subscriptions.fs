[<RequireQualifiedAccess>]
module TwoPoint.Admin.Subscriptions

type Filter =
  | All
  | Active
  | Inactive

type Subscriptions =
  | Waiting
  | Loaded of Api.Subscription list
  | NotLoaded of reason: string

type Deliveries =
  | DeliveriesWaiting
  | DeliveriesLoaded of Api.Delivery list
  | DeliveriesNotLoaded of reason: string

type Action =
  | Cancel
  | Delete

type State = Progress<Action>

type Sheet =
  { Id: string
    Deliveries: Deliveries
    State: State }

type Model =
  { Filter: Filter
    Search: string
    Subscriptions: Subscriptions
    Sheet: Sheet option }

type Msg =
  | Answered of Result<Api.Subscription list, Api.Failure>
  | Filtered of Filter
  | Searched of string
  | Opened of subscriptionId: string
  | Closed
  | DeliveriesAnswered of subscriptionId: string * Result<Api.Delivery list, Api.Failure>
  /// A delete comes here only after the confirmation.
  | Chosen of subscriptionId: string * Action
  | Canceled of subscriptionId: string * Result<Api.Subscription, Api.Failure>
  | Deleted of subscriptionId: string * Result<unit, Api.Failure>

type Effect =
  | LoadDeliveries of subscriptionId: string
  | CancelSubscription of subscriptionId: string
  | DeleteSubscription of subscriptionId: string

type Counts = { Active: int; Inactive: int }

let init =
  { Filter = All
    Search = ""
    Subscriptions = Waiting
    Sheet = None }

let reload (model: Model) =
  { model with
      Subscriptions = Waiting
      Sheet = None }

let filters = [ All, "All"; Active, "Active"; Inactive, "Inactive" ]

/// The author cannot reactivate a subscription, so a canceled subscription has only the delete.
let actions (subscription: Api.Subscription) =
  if subscription.IsActive then [ Cancel; Delete ] else [ Delete ]

let label (action: Action) =
  match action with
  | Cancel -> "Cancel subscription"
  | Delete -> "Delete"

let failure (state: State) =
  match state with
  | Progress.NotDone(Cancel, reason) -> Some $"The cancel did not complete. {reason}"
  | Progress.NotDone(Delete, reason) -> Some $"The delete did not complete. {reason}"
  | Progress.Ready
  | Progress.Sending _ -> None

let private find (subscriptionId: string) (model: Model) =
  match model.Subscriptions with
  | Loaded subscriptions -> subscriptions |> List.tryFind (fun subscription -> subscription.Id = subscriptionId)
  | Waiting
  | NotLoaded _ -> None

let private withState (subscriptionId: string) (state: State) (model: Model) =
  { model with
      Sheet =
        model.Sheet
        |> Option.map (fun sheet -> if sheet.Id = subscriptionId then { sheet with State = state } else sheet) }

let private notDone (subscriptionId: string) (action: Action) (failure: Api.Failure) (model: Model) =
  withState subscriptionId (Progress.NotDone(action, Api.reason failure)) model

let update (msg: Msg) (model: Model) : Model * Effect option =
  match msg, model.Sheet with
  | Answered(Ok subscriptions), _ -> { model with Subscriptions = Loaded subscriptions }, None
  | Answered(Error failure), _ -> { model with Subscriptions = NotLoaded(Api.reason failure) }, None
  | Filtered filter, _ -> { model with Filter = filter }, None
  | Searched search, _ -> { model with Search = search }, None
  | Opened subscriptionId, _ ->
    match find subscriptionId model with
    | Some _ ->
      { model with
          Sheet =
            Some
              { Id = subscriptionId
                Deliveries = DeliveriesWaiting
                State = Progress.Ready } },
      Some(LoadDeliveries subscriptionId)
    | None -> model, None
  | Closed, _ -> { model with Sheet = None }, None
  | DeliveriesAnswered(subscriptionId, result), Some sheet when sheet.Id = subscriptionId ->
    let deliveries =
      match result with
      | Ok deliveries -> DeliveriesLoaded deliveries
      | Error failure -> DeliveriesNotLoaded(Api.reason failure)

    { model with
        Sheet = Some { sheet with Deliveries = deliveries } },
    None
  | DeliveriesAnswered _, _ -> model, None
  | Chosen(subscriptionId, action), Some sheet when sheet.Id = subscriptionId ->
    match sheet.State, find subscriptionId model with
    | (Progress.Ready | Progress.NotDone _), Some subscription when List.contains action (actions subscription) ->
      { model with
          Sheet = Some { sheet with State = Progress.Sending action } },
      Some(
        match action with
        | Cancel -> CancelSubscription subscriptionId
        | Delete -> DeleteSubscription subscriptionId
      )
    | _ -> model, None
  | Chosen _, _ -> model, None
  | Canceled(subscriptionId, Ok canceled), _ ->
    let model = withState subscriptionId Progress.Ready model

    { model with
        Subscriptions =
          match model.Subscriptions with
          | Loaded subscriptions ->
            Loaded(
              subscriptions
              |> List.map (fun subscription -> if subscription.Id = subscriptionId then canceled else subscription)
            )
          | other -> other },
    None
  | Canceled(subscriptionId, Error failure), _ -> notDone subscriptionId Cancel failure model, None
  | Deleted(subscriptionId, Ok()), _ ->
    { model with
        Subscriptions =
          match model.Subscriptions with
          | Loaded subscriptions ->
            Loaded(subscriptions |> List.filter (fun subscription -> subscription.Id <> subscriptionId))
          | other -> other
        Sheet = model.Sheet |> Option.filter (fun sheet -> sheet.Id <> subscriptionId) },
    None
  | Deleted(subscriptionId, Error failure), _ -> notDone subscriptionId Delete failure model, None

let private matches (search: string) (subscription: Api.Subscription) =
  let contains (text: string) =
    text.ToLowerInvariant().Contains(search.ToLowerInvariant())

  contains subscription.Email || (subscription.Name |> Option.exists contains)

let private inFilter (filter: Filter) (subscription: Api.Subscription) =
  match filter with
  | All -> true
  | Active -> subscription.IsActive
  | Inactive -> not subscription.IsActive

let shown (model: Model) =
  let search = model.Search.Trim()

  match model.Subscriptions with
  | Loaded subscriptions ->
    subscriptions
    |> List.filter (fun subscription -> inFilter model.Filter subscription && matches search subscription)
  | Waiting
  | NotLoaded _ -> []

let counts (model: Model) =
  match model.Subscriptions with
  | Loaded subscriptions ->
    let active = subscriptions |> List.filter (fun subscription -> subscription.IsActive) |> List.length

    Some
      { Active = active
        Inactive = List.length subscriptions - active }
  | Waiting
  | NotLoaded _ -> None

let tryOpened (model: Model) =
  model.Sheet |> Option.bind (fun sheet -> find sheet.Id model)

let emptyText (filter: Filter) (search: string) =
  match filter, search.Trim() with
  | _, "" ->
    match filter with
    | All -> "The blog has no subscriptions."
    | Active -> "The blog has no active subscriptions."
    | Inactive -> "The blog has no inactive subscriptions."
  | _ -> "No subscription matches the search."

let statusText (subscription: Api.Subscription) =
  if subscription.IsActive then "Active" else "Inactive"

let deliveryText (deliveryType: Api.DeliveryType) =
  match deliveryType with
  | Api.DeliveryType.Welcome -> "Welcome"
  | Api.DeliveryType.NewPost -> "New post"
