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
    Check: Api.Check option
    /// A save was asked for while another save was in flight.
    SaveAgain: bool
    /// The page that the author wants to go to. The editor stays until the API has the newest text.
    LeaveTo: string option
    /// The top bar fades until the pointer moves.
    Typing: bool
    Details: Details.Model
    Preview: Preview.State
    Publish: Publish.Model
    Deploy: Deploy.Model }

type Model =
  | Loading of slug: string * page: int
  | Loaded of Document
  | NotLoaded of reason: string

type SaveRequest =
  { Slug: string
    ETag: string option
    Fields: Fields
    Details: Api.PostDetails
    Version: int }

type SlugRequest =
  { Slug: string
    ETag: string
    NewSlug: string }

type PreviewRequest =
  { Slug: string
    Fields: Fields
    Details: Api.PostDetails }

type PublishRequest =
  { Slug: string
    ETag: string
    Target: Api.PublishTarget }

type Msg =
  | Answered of slug: string * Result<Api.PostDocument, Api.Failure>
  | TitleChanged of string
  | ExcerptChanged of string
  | BodyChanged of string
  | AutosaveElapsed of version: int
  | SaveRequested
  | SaveAnswered of slug: string * version: int * Result<Api.SavedPost, Api.Failure>
  | PointerMoved
  | LeaveRequested of path: string
  | DetailsMsg of Details.Msg
  | SlugChangeRequested
  | SlugChangeAnswered of slug: string * Result<Api.ChangedSlug, Api.Failure>
  | PreviewToggled
  | PreviewClosed
  | PreviewAnswered of slug: string * Result<Api.PostPreview, Api.Failure>
  | ProblemChosen of Problem.Target
  | PublishClicked
  | PublishChoiceChanged of Publish.When
  | PublishCheckAnswered of slug: string * Publish.When * Result<Api.PublishCheck, Api.Failure>
  | PublishConfirmed
  | PublishAnswered of slug: string * Result<Api.PublishedPost, Api.Failure>
  | PublishClosed
  | PublishNoticeDismissed
  | DeployMsg of slug: string * Deploy.Msg

type Effect =
  /// Starts a timer of `autosaveDelay` milliseconds. The timer sends `AutosaveElapsed`.
  | Autosave of version: int
  | Save of SaveRequest
  | LoadDetailsOptions
  | ChangeSlug of SlugRequest
  | FetchPreview of PreviewRequest
  | CheckPublish of PublishRequest * choice: Publish.When
  | PublishPost of PublishRequest
  | Focus of elementId: string
  | MoveToBodyLine of line: int
  | ReadDeploy of slug: string
  /// Starts a timer of `Deploy.pollDelay` milliseconds. The timer sends `DeployMsg(slug, Deploy.PollElapsed)`.
  | WaitToPollDeploy of slug: string * Deploy.Chain
  | RetryDeploy of slug: string

let autosaveDelay = 2000

let init (slug: string) (page: int) = Loading(slug, page)

let private loaded (page: int) (post: Api.PostDocument) =
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
      Check = None
      SaveAgain = false
      LeaveTo = None
      Typing = false
      Details = Details.init post.Slug post.Details
      Preview = Preview.Closed
      Publish = Publish.init
      Deploy = Deploy.init page }

let private deployRead (post: Api.PostDocument) =
  match post.State with
  | Api.Draft -> None
  | Api.Published
  | Api.Upcoming -> Some(ReadDeploy post.Slug)

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
  | _ when Details.isChangingSlug document.Details -> Loaded document, None
  | Unsaved
  | SaveFailed _ -> startSave document
  | Saving _ -> Loaded { document with SaveAgain = true }, None
  | Saved when document.Check = Some Api.CheckFailed -> startSave document
  | Saved
  | Stale -> Loaded document, None

let private answered (version: int) (result: Result<Api.SavedPost, Api.Failure>) (document: Document) =
  match document.Save, result with
  | Saving sent, Ok saved when sent = version ->
    let document =
      { document with
          ETag = Some saved.ETag
          Check = Some saved.Check
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

let private changeDetails (change: Details.Model -> Details.Model) (document: Document) =
  { document with Details = change document.Details }

let private requestSlugChange (document: Document) =
  match document.State, document.ETag, document.Save with
  | _ when Details.isChangingSlug document.Details -> Loaded document, None
  | Api.Draft, Some eTag, Saved when Details.slugChangeable document.Slug document.Details ->
    Loaded(changeDetails Details.slugChangeStarted document),
    Some(
      ChangeSlug
        { Slug = document.Slug
          ETag = eTag
          NewSlug = Details.typedSlug document.Details }
    )
  | Api.Draft, _, Saved -> Loaded document, None
  | Api.Draft, _, _ ->
    Loaded(changeDetails (Details.slugChangeRefused "Wait until the post is saved, and then change the slug.") document),
    None
  | (Api.Published | Api.Upcoming), _, _ ->
    Loaded(changeDetails (Details.slugChangeRefused "The slug of a published post is fixed.") document), None

let private slugChanged (result: Result<Api.ChangedSlug, Api.Failure>) (document: Document) =
  match result with
  | Ok changed ->
    let document =
      { document with
          Slug = changed.Slug
          ETag = Some changed.ETag
          LeaveTo = Some(Route.path (Route.Post changed.Slug))
          Details = Details.slugChanged changed.Slug document.Details }

    if needsSave document then
      startSave { document with Save = Unsaved }
    else
      Loaded document, None
  | Error Api.Stale ->
    Loaded { changeDetails Details.slugChangeStale document with Save = Stale; SaveAgain = false; LeaveTo = None }, None
  | Error failure -> Loaded(changeDetails (Details.slugChangeRefused (Api.reason failure)) document), None
let private togglePreview (document: Document) =
  match document.Preview with
  | Preview.Closed ->
    Loaded { document with Preview = Preview.Loading },
    Some(
      FetchPreview
        { Slug = document.Slug
          Fields = document.Fields
          Details = document.Details.Values }
    )
  | _ -> Loaded { document with Preview = Preview.Closed }, None

let private previewAnswered (result: Result<Api.PostPreview, Api.Failure>) (document: Document) =
  match document.Preview, result with
  | Preview.Loading, Ok preview -> Loaded { document with Preview = Preview.Shown preview }, None
  | Preview.Loading, Error failure -> Loaded { document with Preview = Preview.Failed(Api.reason failure) }, None
  | _ -> Loaded document, None

let private chooseProblem (target: Problem.Target) (document: Document) =
  let closed = { document with Details = fst (Details.update Details.Closed document.Details) }

  match target with
  | Problem.Control(id, Problem.Sheet) -> Loaded document, Some(Focus id)
  | Problem.Control(id, Problem.WritingColumn) -> Loaded closed, Some(Focus id)
  | Problem.BodyLine line -> Loaded closed, Some(MoveToBodyLine line)

let private withPublish (state: Publish.State) (document: Document) =
  { document with Publish = Publish.withState state document.Publish }

let private startCheck (choice: Publish.When) (document: Document) =
  match document.ETag, Publish.target choice with
  | Some eTag, Some target ->
    Loaded(withPublish (Publish.Checking choice) document),
    Some(
      CheckPublish(
        { Slug = document.Slug
          ETag = eTag
          Target = target },
        choice
      )
    )
  | _, None -> Loaded(withPublish (Publish.Choosing choice) document), None
  | None, Some _ -> Loaded(withPublish (Publish.CheckFailed(choice, "The post has no saved text.")) document), None

let canPublish (document: Document) =
  match document.State, document.Save with
  | Api.Draft, (Saved | Unsaved | Saving _ | SaveFailed _) -> not (Details.isChangingSlug document.Details)
  | Api.Draft, Stale
  | (Api.Published | Api.Upcoming), _ -> false

let private publishClicked (document: Document) =
  match document.State, document.Save with
  | Api.Draft, _ when Details.isChangingSlug document.Details -> Loaded document, None
  | Api.Draft, Saved -> startCheck Publish.Now document
  | Api.Draft, Saving _ -> Loaded(withPublish Publish.Saving document), None
  | Api.Draft, (Unsaved | SaveFailed _) -> startSave (withPublish Publish.Saving document)
  | Api.Draft, Stale
  | (Api.Published | Api.Upcoming), _ -> Loaded document, None

let private resumePublish (model: Model, effect: Effect option) =
  match model with
  | Loaded document ->
    match document.Publish.State, document.Save with
    | Publish.Saving, Saved -> startCheck Publish.Now document
    | Publish.Saving, SaveFailed reason ->
      Loaded(withPublish (Publish.CheckFailed(Publish.Now, $"The post did not save. {reason}")) document), None
    | Publish.Saving, Stale ->
      Loaded(withPublish (Publish.CheckFailed(Publish.Now, "Another tab saved newer text. Reload the page.")) document),
      None
    | Publish.Saving, (Unsaved | Saving _)
    | (Publish.Closed | Publish.Choosing _ | Publish.Checking _ | Publish.Checked _ | Publish.CheckFailed _ | Publish.Sending _ | Publish.Failed _), _ ->
      model, effect
  | Loading _
  | NotLoaded _ -> model, effect

let private publishChoiceChanged (choice: Publish.When) (document: Document) =
  match document.Publish.State with
  | Publish.Closed
  | Publish.Saving
  | Publish.Sending _ -> Loaded document, None
  | Publish.Choosing _
  | Publish.Checking _
  | Publish.Checked _
  | Publish.CheckFailed _
  | Publish.Failed _ -> startCheck choice document

let private publishCheckAnswered
  (choice: Publish.When)
  (result: Result<Api.PublishCheck, Api.Failure>)
  (document: Document)
  =
  match document.Publish.State, result with
  | Publish.Checking current, Ok check when current = choice ->
    Loaded(withPublish (Publish.Checked(choice, check.Problems)) document), None
  | Publish.Checking current, Error failure when current = choice ->
    Loaded(withPublish (Publish.CheckFailed(choice, Api.reason failure)) document), None
  | _ -> Loaded document, None

let private publishConfirmed (document: Document) =
  match document.Publish.State, document.ETag with
  | Publish.Checked(choice, _), Some eTag when Publish.canConfirm document.Publish ->
    match Publish.target choice with
    | Some target ->
      Loaded(withPublish (Publish.Sending choice) document),
      Some(
        PublishPost
          { Slug = document.Slug
            ETag = eTag
            Target = target }
      )
    | None -> Loaded document, None
  | _ -> Loaded document, None

let private publishAnswered (result: Result<Api.PublishedPost, Api.Failure>) (document: Document) =
  match document.Publish.State, result with
  | Publish.Sending _, Ok published ->
    let pendingEdit, eTag =
      if List.contains Api.NewerTextKept published.Warnings then
        true, None
      elif List.contains Api.PendingEditKept published.Warnings then
        true, document.ETag
      else
        false, None

    Loaded
      { document with
          State = published.State
          PublishDate = Some published.PublishDate
          ETag = eTag
          PendingEdit = pendingEdit
          Check = None
          Publish = Publish.closedWith published.Warnings
          Deploy = Deploy.restart document.Deploy },
    Some(ReadDeploy document.Slug)
  | Publish.Sending choice, Error failure ->
    Loaded(withPublish (Publish.Failed(choice, Api.reason failure)) document), None
  | _ -> Loaded document, None

let private publishClosed (document: Document) =
  match document.Publish.State with
  | Publish.Sending _ -> Loaded document, None
  | _ -> Loaded(withPublish Publish.Closed document), None

let private deployChanged (slug: string) (msg: Deploy.Msg) (document: Document) =
  let deploy, effect = Deploy.update msg document.Deploy

  Loaded { document with Deploy = deploy },
  effect
  |> Option.map (fun effect ->
    match effect with
    | Deploy.ReadStatus -> ReadDeploy slug
    | Deploy.WaitToPoll chain -> WaitToPollDeploy(slug, chain)
    | Deploy.Rebuild -> RetryDeploy slug)

let update (msg: Msg) (model: Model) : Model * Effect option =
  match model, msg with
  | Loading(slug, page), Answered(answered, Ok post) when answered = slug -> loaded page post, deployRead post
  | Loading(slug, _), Answered(answered, Error failure) when answered = slug -> NotLoaded(Api.reason failure), None
  | Loaded document, TitleChanged title -> edit (fun fields -> { fields with Title = title }) document
  | Loaded document, ExcerptChanged excerpt -> edit (fun fields -> { fields with Excerpt = excerpt }) document
  | Loaded document, BodyChanged body -> edit (fun fields -> { fields with Body = body }) document
  | Loaded document, AutosaveElapsed version when version = document.Version -> saveNow document
  | Loaded document, SaveRequested -> saveNow document
  | Loaded document, SaveAnswered(slug, version, result) when slug = document.Slug ->
    answered version result document |> resumePublish
  | Loaded document, PointerMoved -> Loaded { document with Typing = false }, None
  | Loaded document, DetailsMsg msg -> detailsChanged msg document
  | Loaded document, SlugChangeRequested -> requestSlugChange document
  | Loaded document, SlugChangeAnswered(slug, result) when slug = document.Slug -> slugChanged result document
  | Loaded document, LeaveRequested path when needsSave document -> saveNow { document with LeaveTo = Some path }
  | Loaded document, PreviewToggled -> togglePreview document
  | Loaded document, PreviewClosed -> Loaded { document with Preview = Preview.Closed }, None
  | Loaded document, PreviewAnswered(slug, result) when slug = document.Slug -> previewAnswered result document
  | Loaded document, ProblemChosen target -> chooseProblem target document
  | Loaded document, PublishClicked -> publishClicked document
  | Loaded document, PublishChoiceChanged choice -> publishChoiceChanged choice document
  | Loaded document, PublishCheckAnswered(slug, choice, result) when slug = document.Slug ->
    publishCheckAnswered choice result document
  | Loaded document, PublishConfirmed -> publishConfirmed document
  | Loaded document, PublishAnswered(slug, result) when slug = document.Slug -> publishAnswered result document
  | Loaded document, PublishClosed -> publishClosed document
  | Loaded document, PublishNoticeDismissed ->
    Loaded { document with Publish = Publish.dismissed document.Publish }, None
  | Loaded document, DeployMsg(slug, msg) when slug = document.Slug -> deployChanged slug msg document
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
let longDateText (date: string) =
  match (Dates.dateText date).Split '-' with
  | [| year; month; day |] ->
    match Int32.TryParse month, Int32.TryParse day with
    | (true, month), (true, day) when month >= 1 && month <= 12 -> $"{months[month - 1]} {day}, {year}"
    | _ -> date
  | _ -> date

let private stateText (document: Document) =
  match document.State, document.PublishDate with
  | Api.Draft, _ -> "Draft"
  | Api.Published, Some date -> $"Published {longDateText date}"
  | Api.Published, None -> "Published"
  | Api.Upcoming, Some date -> $"Upcoming {longDateText date}"
  | Api.Upcoming, None -> "Upcoming"

let private saveText (document: Document) =
  match document.Save with
  | Saved when document.State = Api.Draft || document.PendingEdit -> Some "saved"
  | Saved -> None
  | Unsaved -> Some "unsaved changes"
  | Saving _ -> Some "saving…"
  | SaveFailed _ -> Some "save failed"
  | Stale -> Some "changed in another tab"

let private deployText (document: Document) =
  match document.State with
  | Api.Draft -> None
  | Api.Published
  | Api.Upcoming -> Deploy.statusText document.Deploy

/// For example `Draft · saved` or `Published Feb 4, 2026 · changes not live · saving…`.
let statusText (document: Document) =
  let notLive =
    if document.State <> Api.Draft && (document.PendingEdit || document.Save <> Saved) then
      Some "changes not live"
    else
      None

  [ Some(stateText document); deployText document; notLive; saveText document ]
  |> List.choose id
  |> String.concat " · "

let canRetryDeploy (document: Document) =
  document.State <> Api.Draft && Deploy.canRetry document.Deploy

let retryFailureText (document: Document) = Deploy.retryFailure document.Deploy

/// The reason for the status text of a failed save, or empty when no save failed.
let failureText (model: Model) =
  match model with
  | Loaded { Save = SaveFailed reason } -> reason
  | Loaded { Save = Stale } -> "Another tab saved newer text. Your text stays here. Copy it, and then reload the page."
  | _ -> ""

let hasProblems (document: Document) =
  match document.Check with
  | Some(Api.Found(_ :: _)) -> true
  | Some(Api.Found [])
  | Some Api.CheckFailed
  | None -> false

let primaryAction (document: Document) =
  match document.State with
  | Api.Draft -> "Publish"
  | Api.Published
  | Api.Upcoming -> "Publish changes"

type Availability =
  | Available
  | Unavailable of reason: string

let private laterVersion = Unavailable "A later version adds this action."

let primaryAvailability (document: Document) =
  match document.State, document.Save with
  | _ when canPublish document -> Available
  | Api.Draft, Stale -> Unavailable "Another tab saved newer text. Reload the page."
  | Api.Draft, _ -> Unavailable "Wait until the slug change ends."
  | (Api.Published | Api.Upcoming), _ ->
    // TODO(https://github.com/tw0po1nt/twopoint-website-staging/issues/260): publish the changes of a published post
    laterVersion

type MenuAction =
  | SaveNow
  | ChangesFromLive
  | DiscardChanges
  | ChangeDate
  | Unpublish
  | Revisions
  | SendToSubscribers
  | Delete

type MenuItem =
  { Action: MenuAction
    Label: string
    Availability: Availability }

let private saveAvailability (document: Document) =
  match document.Save with
  | Unsaved
  | SaveFailed _ -> Available
  | Saved -> Unavailable "The post is saved."
  | Saving _ -> Unavailable "The save runs."
  | Stale -> Unavailable "Another tab saved newer text. Reload the page."

let private availability (document: Document) (action: MenuAction) =
  match action, document.State with
  | SaveNow, _ -> saveAvailability document
  | ChangesFromLive, Api.Draft -> Unavailable "A draft has no published text."
  | DiscardChanges, Api.Draft -> Unavailable "A draft has no pending edit."
  | ChangeDate, (Api.Draft | Api.Published) -> Unavailable "Only an upcoming post can change its date."
  | Unpublish, (Api.Draft | Api.Published) -> Unavailable "You can unpublish only an upcoming post."
  | Revisions, Api.Draft -> Unavailable "A draft has no revisions. The first publish makes one."
  | SendToSubscribers, Api.Draft -> Unavailable "Publish the post first."
  // TODO(https://github.com/tw0po1nt/twopoint-website-staging/issues/260): changes from live and discard changes
  | (ChangesFromLive | DiscardChanges), (Api.Published | Api.Upcoming)
  // TODO(https://github.com/tw0po1nt/twopoint-website-staging/issues/261): change the date and unpublish an upcoming post
  | (ChangeDate | Unpublish), Api.Upcoming
  // TODO(https://github.com/tw0po1nt/twopoint-website-staging/issues/263): list and restore the revisions
  | Revisions, (Api.Published | Api.Upcoming)
  // TODO(https://github.com/tw0po1nt/twopoint-website-staging/issues/270): send a post to the subscribers
  | SendToSubscribers, (Api.Published | Api.Upcoming)
  // TODO(https://github.com/tw0po1nt/twopoint-website-staging/issues/262): delete a post
  | Delete, _ -> laterVersion

let menu (document: Document) : MenuItem list =
  [ SaveNow, "Save"
    ChangesFromLive, "Changes from live"
    DiscardChanges, "Discard changes"
    ChangeDate, "Change the date"
    Unpublish, "Unpublish"
    Revisions, "Revisions"
    SendToSubscribers, "Send to subscribers"
    Delete, "Delete" ]
  |> List.map (fun (action, label) ->
    { Action = action
      Label = label
      Availability = availability document action })

let menuMsg (action: MenuAction) =
  match action with
  | SaveNow -> Some SaveRequested
  | ChangesFromLive
  | DiscardChanges
  | ChangeDate
  | Unpublish
  | Revisions
  | SendToSubscribers
  | Delete -> None
