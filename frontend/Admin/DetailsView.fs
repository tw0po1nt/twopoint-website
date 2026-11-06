[<RequireQualifiedAccess>]
module TwoPoint.Admin.DetailsView

open Browser.Types
open Fable.Core.JsInterop
open Oxpecker.Solid
open Oxpecker.Solid.Aria

let private document (model: Shell.Model) =
  match model.Editor with
  | Some(Editor.Loaded document) -> Some document
  | Some(Editor.Loading _)
  | Some(Editor.NotLoaded _)
  | None -> None

let private empty =
  Details.init
    ""
    { Author = None
      Category = None
      Tags = []
      Series = None
      Image = None
      ShowOnHomepage = true }

let private details (model: Shell.Model) =
  document model |> Option.map _.Details |> Option.defaultValue empty

let private slug (model: Shell.Model) =
  document model |> Option.map _.Slug |> Option.defaultValue ""

let private isDraft (model: Shell.Model) =
  match document model with
  | Some document -> document.State = Api.Draft
  | None -> false

let private canChangeSlug (model: Shell.Model) =
  document model |> Option.exists Editor.canChangeSlug

let private slugSuggestion (model: Shell.Model) =
  document model |> Option.bind Editor.slugSuggestion

let private choices (model: Shell.Model) =
  match (details model).Options with
  | Details.Ready choices -> choices
  | Details.NotAsked
  | Details.Waiting
  | Details.NotLoaded _ ->
    { Terms = { Categories = []; Tags = [] }
      Series = [] }

let private optionsText (model: Shell.Model) =
  match (details model).Options with
  | Details.Waiting -> "Loading suggestions…"
  | Details.NotLoaded reason -> $"Couldn't load the suggestions. {reason}"
  | Details.NotAsked
  | Details.Ready _ -> ""

let private problems (model: Shell.Model) =
  match document model with
  | Some { Check = Some(Api.Found found) } -> found |> List.map (fun problem -> problem, Problem.targetOf problem) |> Array.ofList
  | _ -> [||]

let private checkFailed (model: Shell.Model) =
  match document model with
  | Some { Check = Some Api.CheckFailed } -> true
  | _ -> false

let private typed (event: InputEvent) = (event.target :?> HTMLInputElement).value

[<SolidComponent>]
let private chip (label: string) (remove: unit -> unit) =
  span (class' = "chip") {
    label

    button (type' = "button", ariaLabel = $"Remove {label}", onClick = fun _ -> remove ()) { Icon.view Icon.Close }
  }

[<SolidComponent>]
let private suggestionButton (choose: string -> unit) (term: Api.Term) =
  button (type' = "button", class' = "suggestion", onClick = fun _ -> choose term.Value) {
    span () { term.Title }
    span (class' = "muted") { if term.Posts = 1 then "1 post" else $"{term.Posts} posts" }
  }

[<SolidComponent>]
let private suggestionList (choose: string -> unit) (terms: Api.Term list) =
  div (class' = "suggestions") { For(each = Array.ofList terms) { yield fun term _ -> suggestionButton choose term } }

[<SolidComponent>]
let private slugField (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  // A read of the model in the body of a component would build the field again at each change, and the input would lose its focus.
  let hint () = Details.slugHint (slug (model ())) (details (model ()))
  let suggested () = slugSuggestion (model ()) |> Option.defaultValue ""

  Parts
    .Field(id = "details-slug", label = "Slug", hint = "Used in the post's URL.")
    .Has(
      Fragment() {
        div (class' = "row") {
          Parts
            .Input(
              id = "details-slug",
              value = (fun () -> Details.typedSlug (details (model ()))),
              disabledWhen = (fun () -> not (isDraft (model ()))),
              onInput = (fun event -> dispatch (Shell.Editor(Editor.DetailsMsg(Details.SlugTyped(typed event))))),
              onEnter = fun () -> dispatch (Shell.Editor Editor.SlugChangeRequested)
            )
            .View()

          button (
            type' = "button",
            class' = Parts.buttonClass Parts.Secondary Parts.Medium,
            disabled = not (canChangeSlug (model ())),
            onClick = fun _ -> dispatch (Shell.Editor Editor.SlugChangeRequested)
          ) {
            "Change"
          }
        }

        Show(when' = (suggested () <> "")) {
          div (class' = "row slug-suggestion") {
            span () { $"Suggested: {suggested ()}" }

            button (
              type' = "button",
              class' = Parts.buttonClass Parts.Ghost Parts.Small,
              onClick = fun _ -> dispatch (Shell.Editor(Editor.DetailsMsg(Details.SlugTyped(suggested ()))))
            ) {
              "Use suggestion"
            }
          }
        }

        Show(when' = (hint () <> "")) { p (class' = "err", role = "alert") { hint () } }
      }
    )

[<SolidComponent>]
let private categoryField (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  let send msg = dispatch (Shell.Editor(Editor.DetailsMsg msg))
  let locked () = Details.categoryLocked (details (model ())).Values

  Parts
    .Field(id = "details-category", label = "Category")
    .Has(
      Fragment() {
        Show(when' = locked ()) { p (class' = "hint") { "Posts in a series always use the Series category." } }

        Show(when' = (details (model ())).Values.Category.IsSome) {
          div (class' = "chips") {
            chip
              (Slug.termTitle ((details (model ())).Values.Category |> Option.defaultValue ""))
              (fun () -> if not (locked ()) then send Details.CategoryCleared)
          }
        }

        Parts
          .Input(
            id = "details-category",
            placeholder = "Type a category",
            value = (fun () -> (details (model ())).CategoryInput),
            disabledWhen = locked,
            onInput = (fun event -> send (Details.CategoryTyped(typed event))),
            onEnter = fun () -> send (Details.CategoryChosen (details (model ())).CategoryInput)
          )
          .View()

        Show(when' = not (locked ())) {
          suggestionList
            (Details.CategoryChosen >> send)
            (Details.suggestions
              (choices (model ())).Terms.Categories
              (details (model ())).CategoryInput
              ((details (model ())).Values.Category |> Option.toList))
        }
      }
    )

[<SolidComponent>]
let private tagsField (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  let send msg = dispatch (Shell.Editor(Editor.DetailsMsg msg))

  Parts
    .Field(id = "details-tags", label = "Tags")
    .Has(
      Fragment() {
        div (class' = "chips") {
          For(each = Array.ofList (details (model ())).Values.Tags) {
            yield
              fun tag _ ->
                chip (Slug.termTitle tag) (fun () -> send (Details.TagRemoved(Slug.term tag)))
          }
        }

        Parts
          .Input(
            id = "details-tags",
            placeholder = "Add a tag and press Enter",
            value = (fun () -> (details (model ())).TagInput),
            onInput = (fun event -> send (Details.TagTyped(typed event))),
            onEnter = fun () -> send (Details.TagAdded (details (model ())).TagInput)
          )
          .View()

        suggestionList
          (Details.TagAdded >> send)
          (Details.suggestions
            (choices (model ())).Terms.Tags
            (details (model ())).TagInput
            (details (model ())).Values.Tags)
      }
    )

[<SolidComponent>]
let private seriesOption (current: string) (series: Api.SeriesListing) =
  option (value = series.Slug, selected = (series.Slug = current)) { series.Title }

let private newSeriesValue = "+new"

let private newSeriesForm (model: Shell.Model) =
  match (details model).NewSeries with
  | Details.Writing form -> Some form
  | Details.NoNewSeries
  | Details.Made _ -> None

let private madeText (model: Shell.Model) =
  match (details model).NewSeries with
  | Details.Made saved -> SeriesForm.savedText saved
  | Details.NoNewSeries
  | Details.Writing _ -> ""

[<SolidComponent>]
let private newSeries (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  let send msg = dispatch (Shell.Editor(Editor.DetailsMsg msg))
  let form () = newSeriesForm (model ())
  let busy () = form () |> Option.exists SeriesForm.isBusy

  div (class' = "card card-body") {
    h3 () { "New series" }

    SeriesFormView.fields "new-series" (Details.NewSeriesMsg >> send) form (fun () -> Details.otherSeries (details (model ())))

    div (class' = "row") {
      Parts
        .Button(kind = Parts.Ghost, disabled = busy (), onClick = fun _ -> send Details.NewSeriesClosed)
        .Has(Fragment() { "Cancel" })

      Parts
        .Button(kind = Parts.Primary, disabled = busy (), onClick = fun _ -> send (Details.NewSeriesMsg SeriesForm.SaveClicked))
        .Has(Fragment() { if form () |> Option.exists SeriesForm.isSaving then "Saving…" else "Save series" })
    }
  }

[<SolidComponent>]
let private seriesField (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  let send msg = dispatch (Shell.Editor(Editor.DetailsMsg msg))

  let current () =
    match newSeriesForm (model ()) with
    | Some _ -> newSeriesValue
    | None -> (details (model ())).Values.Series |> Option.defaultValue ""

  let listed () =
    let series = (choices (model ())).Series

    match (details (model ())).Values.Series with
    | Some slug when not (series |> List.exists (fun item -> item.Slug = slug)) -> series @ [ { Slug = slug; Title = slug } ]
    | Some _
    | None -> series

  Parts
    .Field(
      id = "details-series",
      label = "Series",
      hint = "Choosing a series sets the category to Series."
    )
    .Has(
      Fragment() {
        Parts
          .Select(
            id = "details-series",
            value = current,
            disabled = Details.newSeriesSaving (details (model ())),
            onChange =
              fun event ->
                match (event.target :?> HTMLSelectElement).value with
                | "" -> send Details.SeriesRemoved
                | value when value = newSeriesValue -> send Details.NewSeriesClicked
                | slug -> send (Details.SeriesChosen slug)
          )
          .Has(
            Fragment() {
              option (value = "") { "No series" }
              For(each = Array.ofList (listed ())) { yield fun series _ -> seriesOption (current ()) series }
              option (value = newSeriesValue, selected = (current () = newSeriesValue)) { "New series…" }
            }
          )

        Show(when' = (madeText (model ()) <> "")) { p (class' = "hint", role = "status") { madeText (model ()) } }
        Show(when' = (newSeriesForm (model ())).IsSome) { newSeries dispatch model }
      }
    )

[<SolidComponent>]
let private problemText (dispatch: Shell.Msg -> unit) (problem: Api.Problem) (target: Problem.Target option) =
  match target with
  | Some target ->
    span () {
      button (type' = "button", class' = "problem-link", onClick = fun _ -> dispatch (Shell.Editor(Editor.ProblemChosen target))) {
        Preview.problemText problem
      }
    }
  | None -> span () { Preview.problemText problem }

[<SolidComponent>]
let private problemList (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  Fragment() {
    Show(when' = checkFailed (model ())) {
      p (class' = "muted small", role = "status") { "Couldn't check the post. Save again to see any problems." }
    }

    Show(when' = ((problems (model ())).Length > 0)) {
      ul (class' = "preview-problems details-problems", ariaLabel = "Problems") {
        For(each = problems (model ())) {
          yield fun (problem, target) _ ->
            li (class' = $"preview-problem {Preview.severityClass problem}") {
              span (class' = "preview-severity") { $"{Preview.severityText problem} " }
              problemText dispatch problem target
            }
        }
      }
    }
  }

[<SolidComponent>]
let private fields (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  let send msg = dispatch (Shell.Editor(Editor.DetailsMsg msg))
  let values () = (details (model ())).Values

  Fragment() {
    Show(when' = (optionsText (model ()) <> "")) { p (class' = "muted small", role = "status") { optionsText (model ()) } }

    problemList dispatch model
    slugField dispatch model
    categoryField dispatch model
    tagsField dispatch model
    seriesField dispatch model

    Parts
      .Field(id = "details-image", label = "Card image", hint = "An https image URL.")
      .Has(
        Parts
          .Input(
            id = "details-image",
            kind = "url",
            value = (fun () -> (values ()).Image |> Option.defaultValue ""),
            onInput = fun event -> send (Details.ImageChanged(typed event))
          )
          .View()
      )

    Parts
      .Field(id = "details-author", label = "Author")
      .Has(
        Parts
          .Input(
            id = "details-author",
            value = (fun () -> (values ()).Author |> Option.defaultValue ""),
            onInput = fun event -> send (Details.AuthorChanged(typed event))
          )
          .View()
      )

    div (class' = "row switch-row") {
      Parts.switch' "details-show-on-homepage" "Show on the home page" (fun () -> (values ()).ShowOnHomepage) (fun () ->
        send (Details.ShowOnHomepageChanged(not (values ()).ShowOnHomepage)))

      span () { "Show on the home page" }
    }
  }

[<SolidComponent>]
let sheet (dispatch: Shell.Msg -> unit) (model: unit -> Shell.Model) =
  let close () = dispatch (Shell.Editor(Editor.DetailsMsg Details.Closed))

  Parts.sheet
    "details-sheet"
    (fun () -> (details (model ())).Open)
    close
    "Details"
    (Fragment() {
      Parts.sheetHead "Details" close
      Parts.sheetBody (fields dispatch model)
    })

let open' (dispatch: Shell.Msg -> unit) =
  dispatch (Shell.Editor(Editor.DetailsMsg Details.Opened))
