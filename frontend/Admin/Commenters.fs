[<RequireQualifiedAccess>]
module TwoPoint.Admin.Commenters

type Filter =
  | All
  | Only of Api.CommenterStatus

type Commenters =
  | Waiting
  | Loaded of Api.Commenter list
  | NotLoaded of reason: string

type Model =
  { Filter: Filter
    Commenters: Commenters }

type Msg =
  | Answered of Result<Api.Commenter list, Api.Failure>
  | Filtered of Filter

let init =
  { Filter = All
    Commenters = Waiting }

let reload (model: Model) = { model with Commenters = Waiting }

let filters =
  (All, "All")
  :: ([ Api.New; Api.Trusted; Api.Banned ]
      |> List.map (fun status -> Only status, Queue.statusText status))

let update (msg: Msg) (model: Model) : Model =
  match msg with
  | Answered(Ok commenters) -> { model with Commenters = Loaded commenters }
  | Answered(Error failure) -> { model with Commenters = NotLoaded(Api.reason failure) }
  | Filtered filter -> { model with Filter = filter }

let shown (model: Model) =
  match model.Commenters, model.Filter with
  | Loaded commenters, All -> commenters
  | Loaded commenters, Only status -> commenters |> List.filter (fun commenter -> commenter.Status = status)
  | (Waiting | NotLoaded _), _ -> []

let emptyText (filter: Filter) =
  match filter with
  | All -> "The blog has no commenters."
  | Only Api.New -> "The blog has no new commenters."
  | Only Api.Trusted -> "The blog has no trusted commenters."
  | Only Api.Banned -> "The blog has no banned commenters."

