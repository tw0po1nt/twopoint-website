[<RequireQualifiedAccess>]
module TwoPoint.Admin.Details

open System
open System.Text.RegularExpressions

/// The category that a post in a series has.
let seriesCategory = "Series"

/// The slug that the site gives to a category or a tag. It has the same rule as the build.
let termSlug (text: string) =
  let joined = Regex.Replace(text, "(\\S)['‘’‚‛′‵](\\S)", "$1$2").Replace("&", " and ")
  let withoutMarks = Regex.Replace(joined.Normalize(Text.NormalizationForm.FormD), "[̀-ͯ]", "")
  Regex.Replace(withoutMarks.ToLowerInvariant(), "[^a-z0-9_]+", "-").Trim '-'

/// The title that the site shows for a category or a tag. For example, `FSharp` gives `F#`.
let termTitle (text: string) =
  Regex.Replace(Regex.Replace(text, "fsharp", "F#", RegexOptions.IgnoreCase), "csharp", "C#", RegexOptions.IgnoreCase)

/// A post slug has lowercase letters and digits, with a single `-` between them.
let isSlug (text: string) = Regex.IsMatch(text, "^[a-z0-9]+(-[a-z0-9]+)*$")

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
  /// The call that moves the draft is in flight.
  | Changing
  | ChangeFailed of reason: string

type Model =
  { Values: Api.PostDetails
    Open: bool
    Options: Options
    CategoryInput: string
    TagInput: string
    SlugInput: string
    Slug: SlugState }

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
  | ImageChanged of string
  | ShowOnHomepageChanged of bool
  | SlugTyped of string

type Effect = LoadOptions

let init (slug: string) (values: Api.PostDetails) =
  { Values = values
    Open = false
    Options = NotAsked
    CategoryInput = ""
    TagInput = ""
    SlugInput = slug
    Slug = Idle }

let private known (model: Model) : Api.Terms =
  match model.Options with
  | Ready choices -> choices.Terms
  | NotAsked
  | Waiting
  | NotLoaded _ -> { Categories = []; Tags = [] }

/// The text of the known term that has the slug of the typed text, or else the typed text.
/// A text with no letter or digit gives `None`.
let resolve (terms: Api.Term list) (typed: string) : string option =
  match termSlug typed with
  | "" -> None
  | slug ->
    match terms |> List.tryFind (fun term -> term.Slug = slug) with
    | Some term -> Some term.Value
    | None -> Some(typed.Trim())

/// The category is `Series` while the post has a series, and the author cannot change it.
let categoryLocked (values: Api.PostDetails) = values.Series.IsSome

/// The terms that match the typed text and that the post does not have yet. The most used terms come first.
let suggestions (terms: Api.Term list) (typed: string) (chosen: string list) : Api.Term list =
  let wanted = termSlug typed
  let taken = chosen |> List.map termSlug

  terms
  |> List.filter (fun term -> not (List.contains term.Slug taken) && (wanted = "" || term.Slug.Contains wanted))
  |> List.truncate 6

let private blankAsNone (text: string) =
  if String.IsNullOrWhiteSpace text then None else Some(text.Trim())

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
    // The category picker does not offer `Series`, so a typed `Series` does not set it either.
    match resolve (known model).Categories typed with
    | Some title when termSlug title <> termSlug seriesCategory -> withCategory (Some title) model, None
    | Some _
    | None -> { model with CategoryInput = "" }, None
  | CategoryCleared when not (categoryLocked values) -> withCategory None model, None
  | CategoryTyped _
  | CategoryChosen _
  | CategoryCleared -> model, None
  | TagTyped typed -> { model with TagInput = typed }, None
  | TagAdded typed ->
    match resolve (known model).Tags typed with
    | Some title when not (values.Tags |> List.exists (fun tag -> termSlug tag = termSlug title)) ->
      { model with
          Values = { values with Tags = values.Tags @ [ title ] }
          TagInput = "" },
      None
    | Some _
    | None -> { model with TagInput = "" }, None
  | TagRemoved slug ->
    { model with Values = { values with Tags = values.Tags |> List.filter (fun tag -> termSlug tag <> slug) } }, None
  | SeriesChosen slug ->
    { model with
        Values =
          { values with
              Series = Some slug
              Category = Some seriesCategory }
        CategoryInput = "" },
    None
  | SeriesRemoved ->
    { model with
        Values =
          { values with
              Series = None
              Category = None } },
    None
  | ImageChanged image -> { model with Values = { values with Image = blankAsNone image } }, None
  | ShowOnHomepageChanged shown -> { model with Values = { values with ShowOnHomepage = shown } }, None
  | SlugTyped typed -> { model with SlugInput = typed; Slug = Idle }, None

/// The text of the slug field is a slug that differs from the current slug.
let slugChangeable (slug: string) (model: Model) =
  model.SlugInput <> slug && isSlug model.SlugInput

let slugHint (slug: string) (model: Model) =
  match model.Slug with
  | ChangeFailed reason -> reason
  | Changing -> ""
  | Idle when model.SlugInput = slug || isSlug model.SlugInput -> ""
  | Idle -> "Use lowercase letters and digits, with a single - between them."
