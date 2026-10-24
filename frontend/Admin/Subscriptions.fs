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

type Sheet = { Id: string; Deliveries: Deliveries }

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

type Effect = LoadDeliveries of subscriptionId: string

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

let private find (subscriptionId: string) (model: Model) =
  match model.Subscriptions with
  | Loaded subscriptions -> subscriptions |> List.tryFind (fun subscription -> subscription.Id = subscriptionId)
  | Waiting
  | NotLoaded _ -> None

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
                Deliveries = DeliveriesWaiting } },
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
