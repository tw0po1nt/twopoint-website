[<RequireQualifiedAccess>]
module TwoPoint.Admin.SeriesForm

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

type Problem =
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

type Msg =
  | TitleTyped of string
  | SlugTyped of string
  | ExcerptTyped of string
  | ImageTyped of string
  | ImagePicked of Browser.Types.File
  | ImageUploaded of Result<string, Api.Failure>
  | SaveClicked

type SaveRequest =
  { Slug: string
    ETag: string option
    Fields: Api.SeriesFields }

type Effect =
  | UploadImage of Browser.Types.File
  | SaveSeries of SaveRequest

let blank =
  { Original = None
    Title = ""
    Slug = ""
    SlugSource = FromTitle
    Excerpt = ""
    Image = ""
    Upload = NoUpload
    Shown = TypedOnly
    State = Progress.Ready }

let ofRecord (series: Api.SeriesRecord) =
  { blank with
      Original = Some series
      Title = series.Title
      Slug = series.Slug
      SlugSource = Typed
      Excerpt = series.Excerpt
      Image = series.Image |> Option.defaultValue "" }

/// The slug stays while a published post, a draft, or a pending edit refers to the series.
let slugLocked (form: Form) =
  form.Original |> Option.exists (fun series -> not (List.isEmpty series.Posts))

let private isHttps (text: string) =
  match System.Uri.TryCreate(text.Trim(), System.UriKind.Absolute) with
  | true, uri -> uri.Scheme = "https"
  | _ -> false

/// The problems that the form shows. A problem stops the save. `others` gives the slugs of the other series.
let problems (others: string list) (form: Form) =
  [ if form.Title.Trim() = "" then EmptyTitle, TitleField
    if not (Slug.isSlug form.Slug) then BadSlug, SlugField
    elif List.contains form.Slug others then SlugTaken, SlugField
    if form.Excerpt.Trim() = "" then EmptyExcerpt, ExcerptField
    if form.Image.Trim() <> "" && not (isHttps form.Image) then BadImage, ImageField ]
  |> List.filter (fun (problem, _) ->
    match form.Shown, problem with
    | All, _ -> true
    | TypedOnly, (EmptyTitle | EmptyExcerpt) -> false
    | TypedOnly, BadSlug -> form.Slug <> ""
    | TypedOnly, (SlugTaken | BadImage) -> true)

let problemText (problem: Problem) =
  match problem with
  | EmptyTitle -> "Give the series a title."
  | EmptyExcerpt -> "Give the series an excerpt."
  | BadSlug -> "Use lowercase letters and digits, with a single - between them."
  | SlugTaken -> "Another series has this slug."
  | BadImage -> "Use an https URL, or upload an image."

let fieldProblem (others: string list) (form: Form) (field: Field) =
  problems others form
  |> List.tryFind (fun (_, place) -> place = field)
  |> Option.map (fst >> problemText)

let private request (form: Form) : SaveRequest =
  let image = form.Image.Trim()

  { Slug = form.Original |> Option.map _.Slug |> Option.defaultValue form.Slug
    ETag = form.Original |> Option.map _.ETag
    Fields =
      { Slug = form.Slug
        Title = form.Title.Trim()
        Excerpt = form.Excerpt.Trim()
        Image = if image = "" then None else Some image } }

/// The entry of a saved series in a series picker.
let listing (saved: Api.SavedSeries) (form: Form) : Api.SeriesListing =
  { Slug = saved.Slug
    Title = (request form).Fields.Title }

let isBusy (form: Form) =
  Progress.isSending form.State || form.Upload = Uploading

let update (others: string list) (msg: Msg) (form: Form) : Form * Effect option =
  match msg with
  | TitleTyped title ->
    let slug =
      match form.SlugSource with
      | FromTitle -> Slug.term title
      | Typed -> form.Slug

    { form with Title = title; Slug = slug }, None
  | SlugTyped slug when not (slugLocked form) -> { form with Slug = slug; SlugSource = Typed }, None
  | ExcerptTyped excerpt -> { form with Excerpt = excerpt }, None
  | ImageTyped image -> { form with Image = image }, None
  | ImagePicked file when not (isBusy form) -> { form with Upload = Uploading }, Some(UploadImage file)
  | ImageUploaded(Ok url) when form.Upload = Uploading -> { form with Image = url; Upload = NoUpload }, None
  | ImageUploaded(Error failure) when form.Upload = Uploading ->
    { form with Upload = UploadFailed(Api.reason failure) }, None
  | SaveClicked when not (isBusy form) ->
    let shown = { form with Shown = All }

    match problems others shown with
    | [] -> { shown with State = Progress.Sending Save }, Some(SaveSeries(request shown))
    | _ -> shown, None
  | SlugTyped _
  | ImagePicked _
  | ImageUploaded _
  | SaveClicked -> form, None

let private failureReason (form: Form) (failure: Api.Failure) =
  match failure, form.Original with
  | Api.Stale, Some _ -> "The series changed in another place after this page loaded it. Close the form and open the series again."
  | Api.Stale, None -> problemText SlugTaken
  | failure, _ -> Api.reason failure

let isSaving (form: Form) = form.State = Progress.Sending Save

let saveFailed (failure: Api.Failure) (form: Form) =
  { form with State = Progress.NotDone(Save, failureReason form failure) }

let deleteFailed (failure: Api.Failure) (form: Form) =
  { form with State = Progress.NotDone(Delete, failureReason form failure) }

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

let slugHint (form: Form) =
  if slugLocked form then
    "The slug stays while a post refers to the series, so each post keeps its series."
  else
    "The address of the series page is /series/<slug>/."

let private warningText (warning: Api.SeriesWarning) =
  match warning with
  | Api.OldRecordKept -> "The record at the old slug changed in another place, so it stays. Delete it from the list."
  | Api.SeriesRebuild Api.ProductionRebuildFailed -> "The rebuild of the production site did not start. Select Rebuild site on the dashboard."
  | Api.SeriesRebuild Api.StagingRebuildFailed -> "The rebuild of the staging site did not start."
  | Api.SeriesRebuild Api.SiteNotRebuilt -> "The rebuild did not start. Select Rebuild site on the dashboard."
  | Api.SeriesRebuild(Api.CommentRowMissing | Api.PendingEditKept | Api.NewerTextKept | Api.DeployNotTracked) -> ""

let savedText (saved: Api.SavedSeries) =
  let warnings = saved.Warnings |> List.map warningText |> List.filter ((<>) "")
  String.concat " " ($"The series '{saved.Slug}' is saved, and the site rebuilds." :: warnings)
