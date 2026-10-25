[<RequireQualifiedAccess>]
module TwoPoint.Admin.Editor

open System

type Fields =
  { Title: string
    Excerpt: string
    Body: string }

type SaveState =
  /// The API has the fields on the screen.
  | Saved
  | Unsaved
  | Saving of version: int
  | SaveFailed of reason: string
  /// Another tab saved newer text. This page keeps its text and stops saving.
  | Stale

type Document =
  { Slug: string
    State: Api.PostState
    PendingEdit: bool
    PublishDate: string option
    /// The ETag of the working blob. A published post with no pending edit has none.
    ETag: string option
    Fields: Fields
    /// Counts each input. A timer and a save answer name the version that they belong to.
    Version: int
    /// The newest version that the API has. A version after it has text that only this page has.
    Confirmed: int
    Save: SaveState
    /// A save was asked for while another save was in flight.
    SaveAgain: bool
    /// The page that the author wants to go to. The editor stays until the API has the newest text.
    LeaveTo: string option
    /// The top bar fades until the pointer moves.
    Typing: bool
    Details: Details.Model }

type Model =
  | Loading of slug: string
  | Loaded of Document
  | NotLoaded of reason: string

type SaveRequest =
  { Slug: string
    ETag: string option
    Fields: Fields
    Details: Api.PostDetails
    Version: int }

/// `ETag` is the ETag of the working blob that the page read.
type SlugRequest =
  { Slug: string
    ETag: string
    NewSlug: string }

type Msg =
  | Answered of slug: string * Result<Api.PostDocument, Api.Failure>
  | TitleChanged of string
  | ExcerptChanged of string
  | BodyChanged of string
  | AutosaveElapsed of version: int
  | SaveRequested
  | SaveAnswered of slug: string * version: int * Result<string, Api.Failure>
  | PointerMoved
  | LeaveRequested of path: string
  | DetailsMsg of Details.Msg
  | SlugChangeRequested
  | SlugChangeAnswered of slug: string * Result<Api.ChangedSlug, Api.Failure>

type Effect =
  /// Starts a timer of `autosaveDelay` milliseconds. The timer sends `AutosaveElapsed`.
  | Autosave of version: int
  | Save of SaveRequest
  | LoadDetailsOptions
  | ChangeSlug of SlugRequest

let autosaveDelay = 2000

let init (slug: string) = Loading slug

let private loaded (post: Api.PostDocument) =
  Loaded
    { Slug = post.Slug
      State = post.State
      PendingEdit = post.PendingEdit
      PublishDate = post.PublishDate
      ETag = post.ETag
      Fields =
        { Title = post.Title
          Excerpt = post.Excerpt
          Body = post.Body }
      Version = 0
      Confirmed = 0
      Save = Saved
      SaveAgain = false
      LeaveTo = None
      Typing = false
      Details = Details.init post.Slug post.Details }

let private requestOf (document: Document) : SaveRequest =
  { Slug = document.Slug
    ETag = document.ETag
    Fields = document.Fields
    Details = document.Details.Values
    Version = document.Version }

/// A page with a stale save has no text that it can send.
let needsSave (document: Document) =
  document.Version > document.Confirmed && document.Save <> Stale

let private startSave (document: Document) =
  Loaded
    { document with
        Save = Saving document.Version
        SaveAgain = false },
  Some(Save(requestOf document))

let private editWith (change: Document -> Document) (document: Document) =
  let version = document.Version + 1

  let save, effect =
    match document.Save with
    | Stale -> Stale, None
    | Saving _ -> document.Save, Some(Autosave version)
    | Saved
    | Unsaved
    | SaveFailed _ -> Unsaved, Some(Autosave version)

  Loaded
    { change document with
        Version = version
        Save = save
        Typing = true },
  effect

let private edit (change: Fields -> Fields) (document: Document) =
  editWith (fun document -> { document with Fields = change document.Fields }) document

let private saveNow (document: Document) =
  match document.Save with
  | _ when document.Details.Slug = Details.Changing -> Loaded document, None
  | Unsaved
  | SaveFailed _ -> startSave document
  | Saving _ -> Loaded { document with SaveAgain = true }, None
  | Saved
  | Stale -> Loaded document, None

let private answered (version: int) (result: Result<string, Api.Failure>) (document: Document) =
  match document.Save, result with
  | Saving sent, Ok eTag when sent = version ->
    let document =
      { document with
          ETag = Some eTag
          Confirmed = version
          PendingEdit = document.PendingEdit || document.State <> Api.Draft }

    if document.Version = version then
      Loaded { document with Save = Saved }, None
    elif document.SaveAgain || document.LeaveTo.IsSome then
      startSave { document with Save = Unsaved }
    else
      Loaded { document with Save = Unsaved }, Some(Autosave document.Version)
  | Saving sent, Error Api.Stale when sent = version ->
    Loaded
      { document with
          Save = Stale
          SaveAgain = false
          LeaveTo = None },
    None
  | Saving sent, Error failure when sent = version ->
    let retry =
      match failure with
      | Api.Unanswered -> Some(Autosave document.Version)
      | _ -> None

    Loaded
      { document with
          Save = SaveFailed(Api.reason failure)
          SaveAgain = false
          LeaveTo = None },
    retry
  | _ -> Loaded document, None

let private detailsChanged (msg: Details.Msg) (document: Document) =
  let details, effect = Details.update msg document.Details
  let effect = effect |> Option.map (fun Details.LoadOptions -> LoadDetailsOptions)

  if details.Values = document.Details.Values then
    Loaded { document with Details = details }, effect
  else
    editWith (fun document -> { document with Details = details }) document

let private withSlugState (state: Details.SlugState) (document: Document) =
  { document with Details = { document.Details with Slug = state } }

/// Only a draft that the API has in full changes its slug. A change in flight blocks a second change.
let private requestSlugChange (document: Document) =
  match document.State, document.ETag, document.Save, document.Details.Slug with
  | _, _, _, Details.Changing -> Loaded document, None
  | Api.Draft, Some eTag, Saved, _ when Details.slugChangeable document.Slug document.Details ->
    Loaded(withSlugState Details.Changing document),
    Some(
      ChangeSlug
        { Slug = document.Slug
          ETag = eTag
          NewSlug = document.Details.SlugInput }
    )
  | Api.Draft, _, Saved, _ -> Loaded document, None
  | Api.Draft, _, _, _ ->
    Loaded(withSlugState (Details.ChangeFailed "Wait until the post is saved, and then change the slug.") document), None
  | (Api.Published | Api.Upcoming), _, _, _ ->
    Loaded(withSlugState (Details.ChangeFailed "The slug of a published post is fixed.") document), None

/// The page goes to the new address. Text that the author typed during the call is saved under the new slug first.
let private slugChanged (result: Result<Api.ChangedSlug, Api.Failure>) (document: Document) =
  match result with
  | Ok changed ->
    let document =
      { document with
          Slug = changed.Slug
          ETag = Some changed.ETag
          LeaveTo = Some(Route.path (Route.Post changed.Slug))
          Details = { document.Details with Slug = Details.Idle; SlugInput = changed.Slug } }

    if needsSave document then
      startSave { document with Save = Unsaved }
    else
      Loaded document, None
  | Error Api.Stale ->
    Loaded { withSlugState Details.Idle document with Save = Stale; SaveAgain = false; LeaveTo = None }, None
  | Error failure -> Loaded(withSlugState (Details.ChangeFailed(Api.reason failure)) document), None

let update (msg: Msg) (model: Model) : Model * Effect option =
  match model, msg with
  | Loading slug, Answered(answered, Ok post) when answered = slug -> loaded post, None
  | Loading slug, Answered(answered, Error failure) when answered = slug -> NotLoaded(Api.reason failure), None
  | Loaded document, TitleChanged title -> edit (fun fields -> { fields with Title = title }) document
  | Loaded document, ExcerptChanged excerpt -> edit (fun fields -> { fields with Excerpt = excerpt }) document
  | Loaded document, BodyChanged body -> edit (fun fields -> { fields with Body = body }) document
  | Loaded document, AutosaveElapsed version when version = document.Version -> saveNow document
  | Loaded document, SaveRequested -> saveNow document
  | Loaded document, SaveAnswered(slug, version, result) when slug = document.Slug -> answered version result document
  | Loaded document, PointerMoved -> Loaded { document with Typing = false }, None
  | Loaded document, DetailsMsg msg -> detailsChanged msg document
  | Loaded document, SlugChangeRequested -> requestSlugChange document
  | Loaded document, SlugChangeAnswered(slug, result) when slug = document.Slug -> slugChanged result document
  | Loaded document, LeaveRequested path when needsSave document -> saveNow { document with LeaveTo = Some path }
  | _ -> model, None

/// The browser asks before it closes a page with text that the API does not have.
let isAtRisk (model: Model) =
  match model with
  | Loaded document ->
    match document.Save with
    | Saved -> false
    | Unsaved
    | Saving _
    | SaveFailed _
    | Stale -> true
  | Loading _
  | NotLoaded _ -> false

let private months =
  [| "Jan"; "Feb"; "Mar"; "Apr"; "May"; "Jun"; "Jul"; "Aug"; "Sep"; "Oct"; "Nov"; "Dec" |]

/// The API sends each date in ISO 8601 UTC. For example, `2026-02-04T13:00:00Z` gives `Feb 4, 2026`.
let dateText (date: string) =
  match (date.Split 'T' |> Array.head).Split '-' with
  | [| year; month; day |] ->
    match Int32.TryParse month, Int32.TryParse day with
    | (true, month), (true, day) when month >= 1 && month <= 12 -> $"{months[month - 1]} {day}, {year}"
    | _ -> date
  | _ -> date

let private stateText (document: Document) =
  match document.State, document.PublishDate with
  | Api.Draft, _ -> "Draft"
  | Api.Published, Some date -> $"Published {dateText date}"
  | Api.Published, None -> "Published"
  | Api.Upcoming, Some date -> $"Upcoming {dateText date}"
  | Api.Upcoming, None -> "Upcoming"

let private saveText (document: Document) =
  match document.Save with
  | Saved when document.State = Api.Draft || document.PendingEdit -> Some "saved"
  | Saved -> None
  | Unsaved -> Some "unsaved changes"
  | Saving _ -> Some "saving…"
  | SaveFailed _ -> Some "save failed"
  | Stale -> Some "changed in another tab"

/// For example `Draft · saved` or `Published Feb 4, 2026 · changes not live · saving…`.
let statusText (document: Document) =
  let notLive =
    if document.State <> Api.Draft && (document.PendingEdit || document.Save <> Saved) then
      Some "changes not live"
    else
      None

  [ Some(stateText document); notLive; saveText document ] |> List.choose id |> String.concat " · "

/// The reason for the status text of a failed save, or empty when no save failed.
let failureText (model: Model) =
  match model with
  | Loaded { Save = SaveFailed reason } -> reason
  | Loaded { Save = Stale } -> "Another tab saved newer text. Your text stays here. Copy it, and then reload the page."
  | _ -> ""

let primaryAction (document: Document) =
  match document.State with
  | Api.Draft -> "Publish"
  | Api.Published
  | Api.Upcoming -> "Publish changes"
