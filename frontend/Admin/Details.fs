[<RequireQualifiedAccess>]
module TwoPoint.Admin.Details

open System

let seriesCategory = "Series"

type Choices =
  { Terms: Api.Terms
    Series: Api.SeriesListing list }

type Options =
  | NotAsked
  | Waiting
  | Ready of Choices
  | NotLoaded of reason: string

type SlugState =
  | Idle
  /// The author asked for a slug change on a draft with unsaved text. The change starts after the save.
  | WaitingForSave
  | Changing
  | ChangeFailed of reason: string

type NewSeries =
  | NoNewSeries
  | Writing of SeriesForm.Form
  | Made of Api.SavedSeries

type Model =
  { Values: Api.PostDetails
    Open: bool
    Options: Options
    CategoryInput: string
    TagInput: string
    SlugInput: string
    Slug: SlugState
    NewSeries: NewSeries }

type Msg =
  | Opened
  | Closed
  | OptionsAnswered of Result<Choices, Api.Failure>
  | AuthorChanged of string
  | CategoryTyped of string
  | CategoryChosen of string
  | CategoryCleared
  | TagTyped of string
  | TagAdded of string
  | TagRemoved of slug: string
  | SeriesChosen of slug: string
  | SeriesRemoved
  | NewSeriesClicked
  | NewSeriesClosed
  | NewSeriesMsg of SeriesForm.Msg
  | NewSeriesAnswered of Result<Api.SavedSeries, Api.Failure>
  | ImageChanged of string
  | ShowOnHomepageChanged of bool
  | SlugTyped of string

type Effect =
  | LoadOptions
  | UploadSeriesImage of Browser.Types.File
  | SaveSeries of SeriesForm.SaveRequest

let init (slug: string) (values: Api.PostDetails) =
  { Values = values
    Open = false
    Options = NotAsked
    CategoryInput = ""
    TagInput = ""
    SlugInput = slug
    Slug = Idle
    NewSeries = NoNewSeries }

let private known (model: Model) : Api.Terms =
  match model.Options with
  | Ready choices -> choices.Terms
  | NotAsked
  | Waiting
  | NotLoaded _ -> { Categories = []; Tags = [] }

let otherSeries (model: Model) =
  match model.Options with
  | Ready choices -> choices.Series |> List.map _.Slug
  | NotAsked
  | Waiting
  | NotLoaded _ -> []

/// While the new series saves, the picker keeps the form, so that the answer can select the series.
let newSeriesSaving (model: Model) =
  match model.NewSeries with
  | Writing form -> SeriesForm.isSaving form
  | NoNewSeries
  | Made _ -> false

/// A text with no letter or digit gives `None`.
let resolve (terms: Api.Term list) (typed: string) : string option =
  match Slug.term typed with
  | "" -> None
  | slug ->
    match terms |> List.tryFind (fun term -> term.Slug = slug) with
    | Some term -> Some term.Value
    | None -> Some(typed.Trim())

let categoryLocked (values: Api.PostDetails) = values.Series.IsSome

let suggestions (terms: Api.Term list) (typed: string) (chosen: string list) : Api.Term list =
  let wanted = Slug.term typed
  let taken = chosen |> List.map Slug.term

  terms
  |> List.filter (fun term -> not (List.contains term.Slug taken) && (wanted = "" || term.Slug.Contains wanted))
  |> List.truncate 6

let private blankAsNone (text: string) =
  if String.IsNullOrWhiteSpace text then None else Some(text.Trim())

let private withSeries (slug: string) (model: Model) =
  { model with
      Values =
        { model.Values with
            Series = Some slug
            Category = Some seriesCategory }
      CategoryInput = "" }

let private withListing (listing: Api.SeriesListing) (options: Options) =
  match options with
  | Ready choices -> Ready { choices with Series = choices.Series @ [ listing ] |> List.sortBy _.Slug }
  | NotAsked
  | Waiting
  | NotLoaded _ -> options

let private withCategory (title: string option) (model: Model) =
  { model with
      Values = { model.Values with Category = title }
      CategoryInput = "" }

let update (msg: Msg) (model: Model) : Model * Effect option =
  let values = model.Values

  match msg with
  | Opened ->
    match model.Options with
    | NotAsked
    | NotLoaded _ -> { model with Open = true; Options = Waiting }, Some LoadOptions
    | Waiting
    | Ready _ -> { model with Open = true }, None
  | Closed -> { model with Open = false }, None
  | OptionsAnswered(Ok choices) -> { model with Options = Ready choices }, None
  | OptionsAnswered(Error failure) -> { model with Options = NotLoaded(Api.reason failure) }, None
  | AuthorChanged author -> { model with Values = { values with Author = blankAsNone author } }, None
  | CategoryTyped typed when not (categoryLocked values) -> { model with CategoryInput = typed }, None
  | CategoryChosen typed when not (categoryLocked values) ->
    match resolve (known model).Categories typed with
    | Some title when Slug.term title <> Slug.term seriesCategory -> withCategory (Some title) model, None
    | Some _
    | None -> { model with CategoryInput = "" }, None
  | CategoryCleared when not (categoryLocked values) -> withCategory None model, None
  | CategoryTyped _
  | CategoryChosen _
  | CategoryCleared -> model, None
  | TagTyped typed -> { model with TagInput = typed }, None
  | TagAdded typed ->
    match resolve (known model).Tags typed with
    | Some title when not (values.Tags |> List.exists (fun tag -> Slug.term tag = Slug.term title)) ->
      { model with
          Values = { values with Tags = values.Tags @ [ title ] }
          TagInput = "" },
      None
    | Some _
    | None -> { model with TagInput = "" }, None
  | TagRemoved slug ->
    { model with Values = { values with Tags = values.Tags |> List.filter (fun tag -> Slug.term tag <> slug) } }, None
  | SeriesChosen _
  | SeriesRemoved
  | NewSeriesClosed when newSeriesSaving model -> model, None
  | SeriesChosen slug -> { withSeries slug model with NewSeries = NoNewSeries }, None
  | SeriesRemoved ->
    { model with
        Values =
          { values with
              Series = None
              Category = None }
        NewSeries = NoNewSeries },
    None
  | NewSeriesClicked ->
    match model.NewSeries with
    | Writing _ -> model, None
    | NoNewSeries
    | Made _ -> { model with NewSeries = Writing SeriesForm.blank }, None
  | NewSeriesClosed -> { model with NewSeries = NoNewSeries }, None
  | NewSeriesMsg msg ->
    match model.NewSeries with
    | Writing form ->
      let form, effect = SeriesForm.update (otherSeries model) msg form

      { model with NewSeries = Writing form },
      effect
      |> Option.map (function
        | SeriesForm.UploadImage file -> UploadSeriesImage file
        | SeriesForm.SaveSeries request -> SaveSeries request)
    | NoNewSeries
    | Made _ -> model, None
  | NewSeriesAnswered answer ->
    match model.NewSeries, answer with
    | Writing form, Ok saved when SeriesForm.isSaving form ->
      { withSeries saved.Slug model with
          Options = withListing (SeriesForm.listing saved form) model.Options
          NewSeries = Made saved },
      None
    | Writing form, Error failure when SeriesForm.isSaving form ->
      { model with NewSeries = Writing(SeriesForm.saveFailed failure form) }, None
    | Writing _, _
    | NoNewSeries, _
    | Made _, _ -> model, None
  | ImageChanged image -> { model with Values = { values with Image = blankAsNone image } }, None
  | ShowOnHomepageChanged shown -> { model with Values = { values with ShowOnHomepage = shown } }, None
  | SlugTyped typed -> { model with SlugInput = typed; Slug = Idle }, None

let slugChangeable (slug: string) (model: Model) =
  model.SlugInput <> slug && Slug.isSlug model.SlugInput

let slugHint (slug: string) (model: Model) =
  match model.Slug with
  | ChangeFailed reason -> reason
  | WaitingForSave
  | Changing -> ""
  | Idle when model.SlugInput = slug || Slug.isSlug model.SlugInput -> ""
  | Idle -> "Use lowercase letters, numbers, and single hyphens (like my-post-2)."

let isChangingSlug (model: Model) =
  match model.Slug with
  | WaitingForSave
  | Changing -> true
  | Idle
  | ChangeFailed _ -> false

let typedSlug (model: Model) = model.SlugInput

let slugChangeStarted (model: Model) = { model with Slug = Changing }

let slugChangeWaitsForSave (model: Model) = { model with Slug = WaitingForSave }

let isSlugWaitingForSave (model: Model) = model.Slug = WaitingForSave

let slugChangeRefused (reason: string) (model: Model) = { model with Slug = ChangeFailed reason }

let slugChangeStale (model: Model) = { model with Slug = Idle }

let slugChanged (slug: string) (model: Model) = { model with Slug = Idle; SlugInput = slug }
