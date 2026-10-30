[<RequireQualifiedAccess>]
module TwoPoint.Admin.Series

type Records =
  | Waiting
  | Loaded of Api.SeriesRecord list
  | NotLoaded of reason: string

type SlugSource =
  /// The slug follows the title, until the author types a slug.
  | FromTitle
  | Typed

type ImageUpload =
  | NoUpload
  | Uploading
  | UploadFailed of reason: string

type Action =
  | Save
  | Delete

type Field =
  | TitleField
  | SlugField
  | ExcerptField
  | ImageField

type FormProblem =
  | EmptyTitle
  | EmptyExcerpt
  | BadSlug
  | SlugTaken
  | BadImage

type ProblemsShown =
  /// A new form shows no empty field as a problem until the first save.
  | TypedOnly
  | All

type Form =
  { Original: Api.SeriesRecord option
    Title: string
    Slug: string
    SlugSource: SlugSource
    Excerpt: string
    Image: string
    Upload: ImageUpload
    Shown: ProblemsShown
    State: Progress<Action> }

type Notice =
  | NoNotice
  | SeriesSaved of Api.SavedSeries
  | SeriesDeleted of slug: string

type Model =
  { Records: Records
    Form: Form option
    Notice: Notice }

type Msg =
  | Answered of Result<Api.SeriesRecord list, Api.Failure>
  | NewClicked
  | Opened of slug: string
  | Closed
  | TitleTyped of string
  | SlugTyped of string
  | ExcerptTyped of string
  | ImageTyped of string
  | ImagePicked of Browser.Types.File
  | ImageUploaded of Result<string, Api.Failure>
  | SaveClicked
  | SaveAnswered of Result<Api.SavedSeries, Api.Failure>
  /// A delete comes here only after the confirmation.
  | DeleteChosen
  | DeleteAnswered of slug: string * Result<unit, Api.Failure>

type SaveRequest =
  { Slug: string
    ETag: string option
    Fields: Api.SeriesFields }

type Effect =
  | Load
  | UploadImage of Browser.Types.File
  | SaveSeries of SaveRequest
  | DeleteSeries of slug: string * eTag: string

let init =
  { Records = Waiting
    Form = None
    Notice = NoNotice }

let reload (model: Model) =
  { model with
      Records = Waiting
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

/// The site has a page for the series at its slug while a published or an upcoming post is in the series.
let isLive (post: Api.SeriesPost) =
  match post.State with
  | Api.SeriesPostState.Published
  | Api.SeriesPostState.Upcoming -> true
  | Api.SeriesPostState.Draft
  | Api.SeriesPostState.PendingEdit -> false

let slugLocked (form: Form) =
  form.Original |> Option.exists (fun series -> series.Posts |> List.exists isLive)

let private records (model: Model) =
  match model.Records with
  | Loaded records -> records
  | Waiting
  | NotLoaded _ -> []

let private find (slug: string) (model: Model) =
  records model |> List.tryFind (fun series -> series.Slug = slug)

let private isHttps (text: string) =
  match System.Uri.TryCreate(text.Trim(), System.UriKind.Absolute) with
  | true, uri -> uri.Scheme = "https"
  | _ -> false

let private allProblems (others: Api.SeriesRecord list) (form: Form) =
  [ if form.Title.Trim() = "" then EmptyTitle, TitleField
    if not (Details.isSlug form.Slug) then BadSlug, SlugField
    elif others |> List.exists (fun series -> series.Slug = form.Slug) then SlugTaken, SlugField
    if form.Excerpt.Trim() = "" then EmptyExcerpt, ExcerptField
    if form.Image.Trim() <> "" && not (isHttps form.Image) then BadImage, ImageField ]

let private others (model: Model) (form: Form) =
  let original = form.Original |> Option.map _.Slug
  records model |> List.filter (fun series -> Some series.Slug <> original)

/// The problems that the form shows. A problem stops the save.
let problems (model: Model) (form: Form) =
  allProblems (others model form) form
  |> List.filter (fun (problem, _) ->
    match form.Shown, problem with
    | All, _ -> true
    | TypedOnly, (EmptyTitle | EmptyExcerpt) -> false
    | TypedOnly, BadSlug -> form.Slug <> ""
    | TypedOnly, (SlugTaken | BadImage) -> true)

let problemText (problem: FormProblem) =
  match problem with
  | EmptyTitle -> "Give the series a title."
  | EmptyExcerpt -> "Give the series an excerpt."
  | BadSlug -> "Use lowercase letters and digits, with a single - between them."
  | SlugTaken -> "Another series has this slug."
  | BadImage -> "Use an https URL, or upload an image."

let fieldProblem (model: Model) (field: Field) =
  model.Form
  |> Option.bind (fun form -> problems model form |> List.tryFind (fun (_, place) -> place = field))
  |> Option.map (fst >> problemText)

let private newForm =
  { Original = None
    Title = ""
    Slug = ""
    SlugSource = FromTitle
    Excerpt = ""
    Image = ""
    Upload = NoUpload
    Shown = TypedOnly
    State = Progress.Ready }

let private formOf (series: Api.SeriesRecord) =
  { newForm with
      Original = Some series
      Title = series.Title
      Slug = series.Slug
      SlugSource = Typed
      Excerpt = series.Excerpt
      Image = series.Image |> Option.defaultValue "" }

let private request (form: Form) : SaveRequest =
  let image = form.Image.Trim()

  { Slug = form.Original |> Option.map _.Slug |> Option.defaultValue form.Slug
    ETag = form.Original |> Option.map _.ETag
    Fields =
      { Slug = form.Slug
        Title = form.Title.Trim()
        Excerpt = form.Excerpt.Trim()
        Image = if image = "" then None else Some image } }

let private failureReason (failure: Api.Failure) =
  match failure with
  | Api.Stale -> "The series changed in another place after this page loaded it. Close the form and open the series again."
  | failure -> Api.reason failure

let private isBusy (form: Form) =
  Progress.isSending form.State || form.Upload = Uploading

let private withForm (change: Form -> Form) (model: Model) =
  { model with Form = model.Form |> Option.map change }

let update (msg: Msg) (model: Model) : Model * Effect option =
  match msg, model.Form with
  | Answered(Ok records), _ -> { model with Records = Loaded records }, None
  | Answered(Error failure), _ -> { model with Records = NotLoaded(Api.reason failure) }, None
  | NewClicked, _ ->
    { model with
        Form = Some newForm
        Notice = NoNotice },
    None
  | Opened slug, _ ->
    match find slug model with
    | Some series ->
      { model with
          Form = Some(formOf series)
          Notice = NoNotice },
      None
    | None -> model, None
  | Closed, Some form when Progress.isSending form.State -> model, None
  | Closed, _ -> { model with Form = None }, None
  | TitleTyped title, Some form ->
    let slug =
      match form.SlugSource with
      | FromTitle -> Details.termSlug title
      | Typed -> form.Slug

    { model with Form = Some { form with Title = title; Slug = slug } }, None
  | SlugTyped slug, Some form when not (slugLocked form) ->
    { model with Form = Some { form with Slug = slug; SlugSource = Typed } }, None
  | ExcerptTyped excerpt, Some form -> { model with Form = Some { form with Excerpt = excerpt } }, None
  | ImageTyped image, Some form -> { model with Form = Some { form with Image = image } }, None
  | ImagePicked file, Some form when not (isBusy form) ->
    { model with Form = Some { form with Upload = Uploading } }, Some(UploadImage file)
  | ImageUploaded(Ok url), Some form when form.Upload = Uploading ->
    { model with Form = Some { form with Image = url; Upload = NoUpload } }, None
  | ImageUploaded(Error failure), Some form when form.Upload = Uploading ->
    { model with Form = Some { form with Upload = UploadFailed(Api.reason failure) } }, None
  | SaveClicked, Some form when not (isBusy form) ->
    let shown = { form with Shown = All }

    match problems model shown with
    | [] -> { model with Form = Some { shown with State = Progress.Sending Save } }, Some(SaveSeries(request shown))
    | _ -> { model with Form = Some shown }, None
  | SaveAnswered(Ok saved), Some form when form.State = Progress.Sending Save ->
    { model with
        Records = Waiting
        Form = None
        Notice = SeriesSaved saved },
    Some Load
  | SaveAnswered(Error failure), Some form when form.State = Progress.Sending Save ->
    let reason = failureReason failure

    { model with Form = Some { form with State = Progress.NotDone(Save, reason) } }, None
  | DeleteChosen, Some({ Original = Some series } as form) when not (isBusy form) && List.isEmpty series.Posts ->
    { model with Form = Some { form with State = Progress.Sending Delete } }, Some(DeleteSeries(series.Slug, series.ETag))
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
    let reason = failureReason failure

    { model with Form = Some { form with State = Progress.NotDone(Delete, reason) } }, None
  | (TitleTyped _ | SlugTyped _ | ExcerptTyped _ | ImageTyped _ | ImagePicked _ | ImageUploaded _), _
  | (SaveClicked | SaveAnswered _ | DeleteChosen | DeleteAnswered _), _ -> model, None

let canDelete (form: Form) =
  match form.Original with
  | Some series -> List.isEmpty series.Posts && not (isBusy form)
  | None -> false

let isSaving (form: Form) = form.State = Progress.Sending Save

let isUploading (form: Form) = form.Upload = Uploading

let uploadFailure (form: Form) =
  match form.Upload with
  | UploadFailed reason -> Some $"The image did not upload. {reason}"
  | NoUpload
  | Uploading -> None

let failure (form: Form) =
  match form.State with
  | Progress.NotDone(Save, reason) -> Some $"The series did not save. {reason}"
  | Progress.NotDone(Delete, reason) -> Some $"The series did not go away. {reason}"
  | Progress.Ready
  | Progress.Sending _ -> None

let formTitle (form: Form) =
  match form.Original with
  | Some _ -> "Edit the series"
  | None -> "New series"

let slugHint (form: Form) =
  if slugLocked form then
    "The slug stays while a published post is in the series, so the series page keeps its address."
  else
    "The address of the series page is /series/<slug>/."

let deleteReason (form: Form) =
  match form.Original with
  | Some series when not (List.isEmpty series.Posts) ->
    Some "Posts refer to this series. Remove the series from each post first, and then delete the series."
  | Some _
  | None -> None

let stateText (post: Api.SeriesPost) =
  match post.State with
  | Api.SeriesPostState.Published -> "Published"
  | Api.SeriesPostState.Upcoming -> "Upcoming"
  | Api.SeriesPostState.Draft -> "Draft"
  | Api.SeriesPostState.PendingEdit -> "Pending edit"

let postText (post: Api.SeriesPost) =
  match post.PublishDate with
  | Some date -> $"{Dates.dateText date} · {stateText post}"
  | None -> stateText post

let private warningText (warning: Api.SeriesWarning) =
  match warning with
  | Api.OldRecordKept -> "The record at the old slug changed in another place, so it stays. Delete it from the list."
  | Api.SeriesRebuild Api.ProductionRebuildFailed -> "The rebuild of the production site did not start. Select Rebuild site on the dashboard."
  | Api.SeriesRebuild Api.StagingRebuildFailed -> "The rebuild of the staging site did not start."
  | Api.SeriesRebuild Api.SiteNotRebuilt -> "The rebuild did not start. Select Rebuild site on the dashboard."
  | Api.SeriesRebuild(Api.CommentRowMissing | Api.PendingEditKept | Api.NewerTextKept | Api.DeployNotTracked) -> ""

let noticeText (notice: Notice) =
  match notice with
  | NoNotice -> None
  | SeriesSaved saved ->
    let warnings = saved.Warnings |> List.map warningText |> List.filter ((<>) "")
    Some(String.concat " " ($"The series '{saved.Slug}' is saved, and the site rebuilds." :: warnings))
  | SeriesDeleted slug -> Some $"The series '{slug}' is deleted."

let emptyText = "The blog has no series."
