[<RequireQualifiedAccess>]
module TwoPoint.Admin.Series

type Records =
  | Waiting
  | Loaded of Api.SeriesRecord list
  | NotLoaded of reason: string

type Notice =
  | NoNotice
  | SeriesSaved of Api.SavedSeries
  | SeriesDeleted of slug: string

type Model =
  { Records: Records
    Form: SeriesForm.Form option
    Notice: Notice }

type Msg =
  | Answered of Result<Api.SeriesRecord list, Api.Failure>
  | NewClicked
  | Opened of slug: string
  | Closed
  | FormMsg of SeriesForm.Msg
  | SaveAnswered of Result<Api.SavedSeries, Api.Failure>
  /// A delete comes here only after the confirmation.
  | DeleteChosen
  | DeleteAnswered of slug: string * Result<unit, Api.Failure>

type Effect =
  | Load
  | UploadImage of Browser.Types.File
  | SaveSeries of SeriesForm.SaveRequest
  | DeleteSeries of slug: string * eTag: string

let init =
  { Records = Waiting
    Form = None
    Notice = NoNotice }

/// The rows stay until the answer, because a back from the editor aims its light at the link of the post.
let reload (model: Model) =
  { model with
      Records =
        match model.Records with
        | Loaded records -> Loaded records
        | Waiting
        | NotLoaded _ -> Waiting
      Form = None }

let private stateOrder (post: Api.SeriesPost) =
  match post.State with
  | Api.SeriesPostState.Published
  | Api.SeriesPostState.Upcoming -> 0
  | Api.SeriesPostState.Draft -> 1
  | Api.SeriesPostState.PendingEdit -> 2

/// The posts in publish date order, an upcoming post too, then the drafts and the pending edits by title.
let orderedPosts (series: Api.SeriesRecord) =
  series.Posts
  |> List.sortBy (fun post ->
    stateOrder post, post.PublishDate |> Option.defaultValue "", post.Title.ToLowerInvariant(), post.Slug)

let private records (model: Model) =
  match model.Records with
  | Loaded records -> records
  | Waiting
  | NotLoaded _ -> []

let private find (slug: string) (model: Model) =
  records model |> List.tryFind (fun series -> series.Slug = slug)

let otherSlugs (model: Model) (form: SeriesForm.Form) =
  let original = form.Original |> Option.map _.Slug
  records model |> List.map _.Slug |> List.filter (fun slug -> Some slug <> original)

/// The problems that the form shows. A problem stops the save.
let problems (model: Model) (form: SeriesForm.Form) = SeriesForm.problems (otherSlugs model form) form

let fieldProblem (model: Model) (field: SeriesForm.Field) =
  model.Form
  |> Option.bind (fun form -> SeriesForm.fieldProblem (otherSlugs model form) form field)

let update (msg: Msg) (model: Model) : Model * Effect option =
  match msg, model.Form with
  | Answered(Ok records), _ -> { model with Records = Loaded records }, None
  | Answered(Error failure), _ -> { model with Records = NotLoaded(Api.reason failure) }, None
  | NewClicked, _ ->
    { model with
        Form = Some SeriesForm.blank
        Notice = NoNotice },
    None
  | Opened slug, _ ->
    match find slug model with
    | Some series ->
      { model with
          Form = Some(SeriesForm.ofRecord series)
          Notice = NoNotice },
      None
    | None -> model, None
  | Closed, Some form when Progress.isSending form.State -> model, None
  | Closed, _ -> { model with Form = None }, None
  | FormMsg msg, Some form ->
    let form, effect = SeriesForm.update (otherSlugs model form) msg form

    { model with Form = Some form },
    effect
    |> Option.map (function
      | SeriesForm.UploadImage file -> UploadImage file
      | SeriesForm.SaveSeries request -> SaveSeries request)
  | SaveAnswered(Ok saved), Some form when SeriesForm.isSaving form ->
    { model with
        Records = Waiting
        Form = None
        Notice = SeriesSaved saved },
    Some Load
  | SaveAnswered(Error failure), Some form when SeriesForm.isSaving form ->
    { model with Form = Some(SeriesForm.saveFailed failure form) }, None
  | DeleteChosen, Some({ Original = Some series } as form) when not (SeriesForm.isBusy form) && List.isEmpty series.Posts ->
    { model with Form = Some { form with State = Progress.Sending SeriesForm.Delete } },
    Some(DeleteSeries(series.Slug, series.ETag))
  | DeleteAnswered(slug, Ok()), _ ->
    { model with
        Records =
          match model.Records with
          | Loaded records -> Loaded(records |> List.filter (fun series -> series.Slug <> slug))
          | other -> other
        Form = model.Form |> Option.filter (fun form -> form.Original |> Option.map _.Slug <> Some slug)
        Notice = SeriesDeleted slug },
    None
  | DeleteAnswered(slug, Error failure), Some({ Original = Some series } as form) when series.Slug = slug ->
    { model with Form = Some(SeriesForm.deleteFailed failure form) }, None
  | FormMsg _, None
  | (SaveAnswered _ | DeleteChosen | DeleteAnswered _), _ -> model, None

let canDelete (form: SeriesForm.Form) =
  match form.Original with
  | Some series -> List.isEmpty series.Posts && not (SeriesForm.isBusy form)
  | None -> false

let formTitle (form: SeriesForm.Form) =
  match form.Original with
  | Some _ -> "Edit the series"
  | None -> "New series"

let private stateName (state: Api.SeriesPostState) =
  match state with
  | Api.SeriesPostState.Published -> "published post"
  | Api.SeriesPostState.Upcoming -> "upcoming post"
  | Api.SeriesPostState.Draft -> "draft"
  | Api.SeriesPostState.PendingEdit -> "pending edit"

let private andList (items: string list) =
  match List.rev items with
  | [] -> ""
  | [ only ] -> only
  | last :: rest -> $"""{String.concat ", " (List.rev rest)} and {last}"""

let deleteReason (form: SeriesForm.Form) =
  match form.Original |> Option.map _.Posts with
  | Some [] -> None
  | Some posts ->
    let counts =
      posts
      |> List.countBy _.State
      |> List.sortBy fst
      |> List.map (fun (state, count) -> if count = 1 then $"1 {stateName state}" else $"{count} {stateName state}s")

    Some $"The series has {andList counts}. Remove the series from each post first, and then delete the series."
  | None -> None

let stateText (post: Api.SeriesPost) =
  match post.State with
  | Api.SeriesPostState.Published -> "Published"
  | Api.SeriesPostState.Upcoming -> "Upcoming"
  | Api.SeriesPostState.Draft -> "Draft"
  | Api.SeriesPostState.PendingEdit -> "Pending edit"

let noticeText (notice: Notice) =
  match notice with
  | NoNotice -> None
  | SeriesSaved saved -> Some(SeriesForm.savedText saved)
  | SeriesDeleted slug -> Some $"The series '{slug}' is deleted."

let emptyText = "The blog has no series."
