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

type ChangesDialog =
  | ChangesHidden
  | ChangesShown

type Document =
  { Slug: string
    State: Api.PostState
    PendingEdit: bool
    PublishDate: string option
    /// The ETag of the working blob. A published post with no pending edit has none.
    ETag: string option
    Fields: Fields
    /// Counts each input. A save answer names the version that it belongs to.
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
    Published: Api.PublishedFields option
    Changes: ChangesDialog
    Discard: Confirm.State
    Unpublish: Confirm.State
    UnpublishCheck: RemovalCheck.State
    Delete: Typed.State
    DeleteCheck: RemovalCheck.State
    DeleteDraft: Confirm.State
    ChangeDate: Typed.State
    ChangeWarnings: Api.ChangeWarning list
    Revisions: Revisions.State
    /// The typed text is the intro of the email.
    SendToSubscribers: Typed.State
    /// The count of active subscriptions that the last start queued an email for. The author dismisses it.
    QueuedCount: int option
    DeliveryStarted: string option
    Deploy: Deploy.Model }

type Model =
  | Loading of slug: string * chain: Deploy.Chain
  | Loaded of Document
  | NotLoaded of reason: string

type Availability =
  | Available
  | Unavailable of reason: string

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

type DiscardRequest = { Slug: string; ETag: string }

/// `ETag` is `None` when the post has no pending edit. `Edits` are the pending edits that the unpublish publishes first.
type UnpublishRequest =
  { Slug: string
    ETag: string option
    Edits: Api.RemovalEdit list }

/// `ETag` is `None` when the post has no working blob. `ConfirmSlug` is the typed text of a published post, and `None` for a draft.
/// `Edits` are the pending edits that the delete publishes first.
type DeleteRequest =
  { Slug: string
    ETag: string option
    ConfirmSlug: string option
    Edits: Api.RemovalEdit list }

type ChangeDateRequest =
  { Slug: string
    ETag: string option
    PublishDate: string }

/// `ETag` is `None` when the post has no pending edit.
type RestoreRequest =
  { Slug: string
    ETag: string option
    Revision: string }

type DeliveryRequest = { Slug: string; Intro: string }

type RemovalDialog =
  | DeleteDialog
  | UnpublishDialog

type Msg =
  | Answered of slug: string * Result<Api.PostDocument, Api.Failure>
  | TitleChanged of string
  | ExcerptChanged of string
  | BodyChanged of string
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
  | ChangesOpened
  | ChangesClosed
  | DiscardRequested
  | DiscardConfirmed
  | DiscardAnswered of slug: string * Result<unit, Api.Failure>
  | DiscardClosed
  | UnpublishRequested
  | UnpublishConfirmed
  | UnpublishAnswered of slug: string * Result<Api.UpcomingChange, Api.Failure>
  | UnpublishClosed
  | ChangeDateRequested
  | ChangeDateTyped of string
  | ChangeDateConfirmed
  | ChangeDateAnswered of slug: string * Result<Api.UpcomingChange, Api.Failure>
  | ChangeDateClosed
  | ChangeNoticeDismissed
  | DeleteRequested
  | DeleteTyped of string
  | RemovalCheckAnswered of slug: string * dialog: RemovalDialog * Result<Api.RemovalCheck, Api.Failure>
  | DeleteConfirmed
  | DeleteAnswered of slug: string * Result<Api.DeletedPost, Api.Failure>
  | DeleteClosed
  | RevisionsRequested
  | RevisionsAnswered of slug: string * Result<Api.Revision list, Api.Failure>
  | RestoreChosen of revision: string
  | RestoreAnswered of slug: string * Result<Api.RestoredRevision, Api.Failure>
  | RevisionsClosed
  | SendRequested
  | IntroTyped of string
  | SendConfirmed
  | SendAnswered of slug: string * Result<Api.StartedDelivery, Api.Failure>
  | SendClosed
  | SentNoticeDismissed

type Effect =
  | Save of SaveRequest
  | LoadDetailsOptions
  | UploadSeriesImage of Browser.Types.File
  | SaveSeries of SeriesForm.SaveRequest
  | ChangeSlug of SlugRequest
  | FetchPreview of PreviewRequest
  | CheckPublish of PublishRequest * choice: Publish.When
  | PublishPost of PublishRequest
  | DiscardPending of DiscardRequest
  | UnpublishPost of UnpublishRequest
  | ChangePostDate of ChangeDateRequest
  | CheckRemoval of slug: string * dialog: RemovalDialog
  | DeletePost of DeleteRequest
  | LoadRevisions of slug: string
  | RestoreRevision of RestoreRequest
  | StartDelivery of DeliveryRequest
  | Reload of slug: string
  | Focus of elementId: string
  | MoveToBodyLine of line: int
  | ReadDeploy of slug: string
  /// Starts a timer of `Deploy.pollDelay` milliseconds. The timer sends `DeployMsg(slug, Deploy.PollElapsed)`.
  | WaitToPollDeploy of slug: string * Deploy.Chain
  | RetryDeploy of slug: string

let init (slug: string) (page: int) = Loading(slug, Deploy.firstChain page)

let private loaded (chain: Deploy.Chain) (post: Api.PostDocument) =
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
      Published = post.Published
      Changes = ChangesHidden
      Discard = Confirm.Closed
      Unpublish = Confirm.Closed
      UnpublishCheck = RemovalCheck.NotChecked
      Delete = Typed.Closed
      DeleteCheck = RemovalCheck.NotChecked
      DeleteDraft = Confirm.Closed
      ChangeDate = Typed.Closed
      ChangeWarnings = []
      Revisions = Revisions.Closed
      SendToSubscribers = Typed.Closed
      QueuedCount = None
      DeliveryStarted = post.DeliveryStarted
      Deploy = Deploy.start chain }

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

  let save =
    match document.Save with
    | Stale
    | Saving _ -> document.Save
    | Saved
    | Unsaved
    | SaveFailed _ -> Unsaved

  { change document with
      Version = version
      Save = save
      Typing = true }

let private edit (change: Fields -> Fields) (document: Document) =
  Loaded(editWith (fun document -> { document with Fields = change document.Fields }) document), None

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
      Loaded { document with Save = Unsaved }, None
  | Saving sent, Error Api.Stale when sent = version ->
    Loaded
      { document with
          Save = Stale
          SaveAgain = false
          LeaveTo = None },
    None
  | Saving sent, Error failure when sent = version ->
    Loaded
      { document with
          Save = SaveFailed(Api.reason failure)
          SaveAgain = false
          LeaveTo = None },
    None
  | _ -> Loaded document, None

let private detailsChanged (msg: Details.Msg) (document: Document) =
  let details, effect = Details.update msg document.Details
  let effect =
    effect
    |> Option.map (function
      | Details.LoadOptions -> LoadDetailsOptions
      | Details.UploadSeriesImage file -> UploadSeriesImage file
      | Details.SaveSeries request -> SaveSeries request)

  let withDetails (document: Document) = { document with Details = details }

  let document =
    if details.Values = document.Details.Values then
      withDetails document
    else
      editWith withDetails document

  Loaded document, effect

let private changeDetails (change: Details.Model -> Details.Model) (document: Document) =
  { document with Details = change document.Details }

let private startSlugChange (eTag: string) (document: Document) =
  Loaded(changeDetails Details.slugChangeStarted document),
  Some(
    ChangeSlug
      { Slug = document.Slug
        ETag = eTag
        NewSlug = Details.typedSlug document.Details }
  )

/// A draft with unsaved text saves first. The slug change starts after the save, in `resumeSlugChange`.
let private requestSlugChange (document: Document) =
  match document.State, document.ETag, document.Save with
  | _ when Details.isChangingSlug document.Details || Publish.isOpen document.Publish -> Loaded document, None
  | Api.Draft, _, _ when not (Details.slugChangeable document.Slug document.Details) -> Loaded document, None
  | Api.Draft, Some eTag, Saved -> startSlugChange eTag document
  | Api.Draft, None, Saved -> Loaded document, None
  | Api.Draft, _, (Unsaved | SaveFailed _) -> startSave (changeDetails Details.slugChangeWaitsForSave document)
  | Api.Draft, _, Saving _ -> Loaded(changeDetails Details.slugChangeWaitsForSave document), None
  | Api.Draft, _, Stale ->
    Loaded(
      changeDetails
        (Details.slugChangeRefused "This post changed in another tab. Reload the page to get the latest version.")
        document
    ),
    None
  | (Api.Published | Api.Upcoming), _, _ ->
    Loaded(changeDetails (Details.slugChangeRefused "You can't change the slug of a published post.") document), None

/// After the save answer, a slug change that waits for the save starts. A failed save starts no slug change.
let private resumeSlugChange (model: Model, effect: Effect option) =
  match model with
  | Loaded document when Details.isSlugWaitingForSave document.Details ->
    match document.Save, document.ETag with
    | Saving _, _ -> model, effect
    | (Saved | Unsaved), Some eTag -> startSlugChange eTag document
    | (Saved | Unsaved), None
    | (SaveFailed _ | Stale), _ -> Loaded(changeDetails Details.slugChangeStale document), effect
  | Loaded _
  | Loading _
  | NotLoaded _ -> model, effect

/// Each save state but a stale one can start a slug change. A draft with unsaved text saves first.
let canChangeSlug (document: Document) =
  document.State = Api.Draft
  && document.Save <> Stale
  && not (Details.isChangingSlug document.Details)
  && Details.slugChangeable document.Slug document.Details

/// The slug that the title of a draft gives. The suggestion hides when it is the slug of the draft or the typed slug,
/// and while a slug change is in progress.
let slugSuggestion (document: Document) =
  match document.State, Slug.ofTitle document.Fields.Title with
  | Api.Draft, Some suggested when
    not (Details.isChangingSlug document.Details)
    && suggested <> document.Slug
    && suggested <> Details.typedSlug document.Details
    ->
    Some suggested
  | Api.Draft, _
  | (Api.Published | Api.Upcoming), _ -> None

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
  | None, Some _ -> Loaded(withPublish (Publish.CheckFailed(choice, "Save the post before publishing.")) document), None

let canPublish (document: Document) =
  match document.State, document.Save with
  | _, Stale -> false
  | Api.Draft, _ -> not (Details.isChangingSlug document.Details)
  | (Api.Published | Api.Upcoming), _ -> document.PendingEdit || needsSave document

let firstChoice (document: Document) =
  match document.State with
  | Api.Draft -> Publish.Draft Publish.Now
  | Api.Published
  | Api.Upcoming -> Publish.Changes Publish.KeepDate

let private publishClicked (document: Document) =
  match document.Save with
  | _ when not (canPublish document) -> Loaded document, None
  | Saved -> startCheck (firstChoice document) document
  | Saving _ -> Loaded(withPublish Publish.Saving document), None
  | Unsaved
  | SaveFailed _ -> startSave (withPublish Publish.Saving document)
  | Stale -> Loaded document, None

let private resumePublish (model: Model, effect: Effect option) =
  match model with
  | Loaded document ->
    match document.Publish.State, document.Save with
    | Publish.Saving, Saved -> startCheck (firstChoice document) document
    | Publish.Saving, SaveFailed reason ->
      Loaded(withPublish (Publish.CheckFailed(firstChoice document, $"Couldn't save the post. {reason}")) document), None
    | Publish.Saving, Stale ->
      Loaded(
        withPublish (Publish.CheckFailed(firstChoice document, "This post changed in another tab. Reload the page to get the latest version.")) document
      ),
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
          Published =
            Some
              { Title = document.Fields.Title
                Excerpt = document.Fields.Excerpt
                Body = document.Fields.Body
                Details = document.Details.Values }
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

let private requireSaved (save: SaveState) =
  match save with
  | Stale -> Unavailable "This post changed in another tab. Reload the page to get the latest version."
  | Unsaved
  | SaveFailed _ -> Unavailable "Save the post first."
  | Saving _ -> Unavailable "Saving…"
  | Saved -> Available

let private discardAvailability (document: Document) =
  match document.State, requireSaved document.Save with
  | Api.Draft, _ -> Unavailable "Only for published posts."
  | _, (Unavailable _ as notSaved) -> notSaved
  | _, Available when not document.PendingEdit -> Unavailable "No changes to discard."
  | _, Available when document.ETag.IsNone -> Unavailable "Reload the page, then try again."
  | _, Available -> Available

let private discardRequested (document: Document) =
  match discardAvailability document with
  | Available -> Loaded { document with Discard = Confirm.Confirming }, None
  | Unavailable _ -> Loaded document, None

let private discardConfirmed (document: Document) =
  match document.Discard, document.ETag with
  | (Confirm.Confirming | Confirm.Failed _), Some eTag ->
    Loaded { document with Discard = Confirm.Sending },
    Some(DiscardPending { Slug = document.Slug; ETag = eTag })
  | _ -> Loaded document, None

let private discardAnswered (result: Result<unit, Api.Failure>) (document: Document) =
  match document.Discard, result with
  | Confirm.Sending, Ok() -> Loading(document.Slug, Deploy.nextChain document.Deploy), Some(Reload document.Slug)
  | Confirm.Sending, Error failure -> Loaded { document with Discard = Confirm.Failed(Api.reason failure) }, None
  | _ -> Loaded document, None

let private discardClosed (document: Document) =
  if Confirm.isSending document.Discard then
    Loaded document, None
  else
    Loaded { document with Discard = Confirm.Closed }, None

let private upcomingChangeAvailability (document: Document) =
  match document.State, requireSaved document.Save with
  | (Api.Draft | Api.Published), _ -> Unavailable "Only for upcoming posts."
  | _, (Unavailable _ as notSaved) -> notSaved
  | _, Available when document.PendingEdit && document.ETag.IsNone -> Unavailable "Reload the page, then try again."
  | _, Available -> Available

let private unpublishRequested (document: Document) =
  match upcomingChangeAvailability document with
  | Available ->
    Loaded
      { document with
          Unpublish = Confirm.Confirming
          UnpublishCheck = RemovalCheck.Checking },
    Some(CheckRemoval(document.Slug, UnpublishDialog))
  | Unavailable _ -> Loaded document, None

let canUnpublish (document: Document) =
  match document.Unpublish with
  | Confirm.Confirming
  | Confirm.Failed _ -> RemovalCheck.allows document.UnpublishCheck
  | Confirm.Closed
  | Confirm.Sending -> false

let unpublishButtonText (document: Document) =
  RemovalCheck.buttonText "Unpublish" "unpublish" document.UnpublishCheck

let private unpublishConfirmed (document: Document) =
  if canUnpublish document then
    Loaded { document with Unpublish = Confirm.Sending },
    Some(
      UnpublishPost
        { Slug = document.Slug
          ETag = document.ETag
          Edits = RemovalCheck.edits document.UnpublishCheck }
    )
  else
    Loaded document, None

let private afterChange (change: Api.UpcomingChange) (document: Document) =
  { document with
      State = change.State
      PublishDate = change.PublishDate
      Check = None
      ChangeWarnings = change.Warnings
      Deploy = Deploy.restart document.Deploy }

let private unpublishAnswered (result: Result<Api.UpcomingChange, Api.Failure>) (document: Document) =
  match document.Unpublish, result with
  | Confirm.Sending, Ok change ->
    Loaded
      { afterChange change document with
          ETag = change.ETag
          PendingEdit = false
          Published = None
          Unpublish = Confirm.Closed
          UnpublishCheck = RemovalCheck.NotChecked },
    Some(ReadDeploy document.Slug)
  | Confirm.Sending, Error failure ->
    Loaded
      { document with
          Unpublish = Confirm.Failed(Api.reason failure)
          UnpublishCheck = RemovalCheck.Checking },
    Some(CheckRemoval(document.Slug, UnpublishDialog))
  | _ -> Loaded document, None

let private unpublishClosed (document: Document) =
  if Confirm.isSending document.Unpublish then
    Loaded document, None
  else
    Loaded
      { document with
          Unpublish = Confirm.Closed
          UnpublishCheck = RemovalCheck.NotChecked },
    None

let private changeDateRequested (document: Document) =
  match upcomingChangeAvailability document with
  | Available ->
    Loaded { document with ChangeDate = Typed.Typing(ChangeDate.initial document.PublishDate) }, None
  | Unavailable _ -> Loaded document, None

let private changeDateTyped (typed: string) (document: Document) =
  match document.ChangeDate with
  | Typed.Typing _
  | Typed.Failed _ -> Loaded { document with ChangeDate = Typed.Typing typed }, None
  | Typed.Closed
  | Typed.Sending _ -> Loaded document, None

let private changeDateConfirmed (document: Document) =
  match ChangeDate.canConfirm document.ChangeDate, ChangeDate.iso document.ChangeDate with
  | true, Some iso ->
    Loaded { document with ChangeDate = Typed.Sending(Typed.typed document.ChangeDate) },
    Some(ChangePostDate { Slug = document.Slug; ETag = document.ETag; PublishDate = iso })
  | _ -> Loaded document, None

let private changeDateAnswered (result: Result<Api.UpcomingChange, Api.Failure>) (document: Document) =
  match document.ChangeDate, result with
  | Typed.Sending _, Ok change ->
    Loaded
      { afterChange change document with
          ETag = (if document.PendingEdit then change.ETag else document.ETag)
          ChangeDate = Typed.Closed },
    Some(ReadDeploy document.Slug)
  | Typed.Sending typed, Error failure ->
    Loaded { document with ChangeDate = Typed.Failed(typed, Api.reason failure) }, None
  | _ -> Loaded document, None

let private changeDateClosed (document: Document) =
  if Typed.isSending document.ChangeDate then
    Loaded document, None
  else
    Loaded { document with ChangeDate = Typed.Closed }, None

/// A published post needs its typed slug. A draft needs a plain confirm.
type DeleteConfirm =
  | PlainConfirm
  | TypeSlug

type DeleteStep =
  | NoDeleteDialog
  | CheckingDelete
  /// The check is blocked or did not run.
  | DeleteBlocked
  | WaitingForSlug
  | ReadyToDelete
  | DeleteRuns

let deleteConfirm (document: Document) =
  match document.State with
  | Api.Draft -> PlainConfirm
  | Api.Published
  | Api.Upcoming -> TypeSlug

let isDeleteOpen (document: Document) = Confirm.isOpen document.DeleteDraft || Typed.isOpen document.Delete

let deleteReason (document: Document) =
  match deleteConfirm document with
  | PlainConfirm -> Confirm.reason document.DeleteDraft
  | TypeSlug -> Typed.reason document.Delete

let deleteStep (document: Document) =
  match deleteConfirm document, document.DeleteDraft, document.Delete with
  | PlainConfirm, (Confirm.Confirming | Confirm.Failed _), _ -> ReadyToDelete
  | PlainConfirm, Confirm.Sending, _
  | TypeSlug, _, Typed.Sending _ -> DeleteRuns
  | PlainConfirm, Confirm.Closed, _
  | TypeSlug, _, Typed.Closed -> NoDeleteDialog
  | TypeSlug, _, (Typed.Typing typed | Typed.Failed(typed, _)) ->
    match document.DeleteCheck with
    | RemovalCheck.NotChecked
    | RemovalCheck.Checking -> CheckingDelete
    | RemovalCheck.Failed _
    | RemovalCheck.Checked(Api.Blocked _) -> DeleteBlocked
    | RemovalCheck.Checked(Api.PlainRemoval | Api.PublishAndRemove _) ->
      if typed = document.Slug then ReadyToDelete else WaitingForSlug

/// The pending edits that the delete publishes first.
let deleteEdits (document: Document) = RemovalCheck.edits document.DeleteCheck

let deleteProblems (document: Document) = RemovalCheck.problems document.DeleteCheck

let deleteButtonText (document: Document) =
  RemovalCheck.buttonText "Delete" "delete" document.DeleteCheck

let private deleteAvailability (document: Document) =
  match requireSaved document.Save with
  | Unavailable _ as notSaved -> notSaved
  | Available when document.ETag.IsNone && (document.State = Api.Draft || document.PendingEdit) ->
    Unavailable "Reload the page, then try again."
  | Available -> Available

let private deleteRequested (document: Document) =
  match deleteAvailability document with
  | Unavailable _ -> Loaded document, None
  | Available ->
    match deleteConfirm document with
    | PlainConfirm -> Loaded { document with DeleteDraft = Confirm.Confirming }, None
    | TypeSlug ->
      Loaded
        { document with
            Delete = Typed.Typing ""
            DeleteCheck = RemovalCheck.Checking },
      Some(CheckRemoval(document.Slug, DeleteDialog))

let private deleteTyped (typed: string) (document: Document) =
  match document.Delete with
  | Typed.Typing _
  | Typed.Failed _ -> Loaded { document with Delete = Typed.Typing typed }, None
  | Typed.Closed
  | Typed.Sending _ -> Loaded document, None

let private removalCheckAnswered (dialog: RemovalDialog) (result: Result<Api.RemovalCheck, Api.Failure>) (document: Document) =
  match dialog with
  | DeleteDialog when Typed.isOpen document.Delete ->
    Loaded { document with DeleteCheck = RemovalCheck.answered result document.DeleteCheck }, None
  | UnpublishDialog when Confirm.isOpen document.Unpublish ->
    Loaded { document with UnpublishCheck = RemovalCheck.answered result document.UnpublishCheck }, None
  | DeleteDialog
  | UnpublishDialog -> Loaded document, None

let private deleteConfirmed (document: Document) =
  match deleteStep document, deleteConfirm document with
  | ReadyToDelete, PlainConfirm ->
    Loaded { document with DeleteDraft = Confirm.Sending },
    Some(
      DeletePost
        { Slug = document.Slug
          ETag = document.ETag
          ConfirmSlug = None
          Edits = [] }
    )
  | ReadyToDelete, TypeSlug ->
    let typed = Typed.typed document.Delete

    Loaded { document with Delete = Typed.Sending typed },
    Some(
      DeletePost
        { Slug = document.Slug
          ETag = document.ETag
          ConfirmSlug = Some typed
          Edits = deleteEdits document }
    )
  | (NoDeleteDialog | CheckingDelete | DeleteBlocked | WaitingForSlug | DeleteRuns), _ -> Loaded document, None

let private deleteAnswered (result: Result<Api.DeletedPost, Api.Failure>) (document: Document) =
  match deleteStep document, deleteConfirm document, result with
  | DeleteRuns, PlainConfirm, Ok _ -> Loaded { document with DeleteDraft = Confirm.Closed }, None
  | DeleteRuns, TypeSlug, Ok _ ->
    Loaded
      { document with
          Delete = Typed.Closed
          DeleteCheck = RemovalCheck.NotChecked },
    None
  | DeleteRuns, PlainConfirm, Error failure ->
    Loaded { document with DeleteDraft = Confirm.Failed(Api.reason failure) }, None
  | DeleteRuns, TypeSlug, Error failure ->
    Loaded
      { document with
          Delete = Typed.Failed(Typed.typed document.Delete, Api.reason failure)
          DeleteCheck = RemovalCheck.Checking },
    Some(CheckRemoval(document.Slug, DeleteDialog))
  | (NoDeleteDialog | CheckingDelete | DeleteBlocked | WaitingForSlug | ReadyToDelete), _, _ -> Loaded document, None

let private deleteClosed (document: Document) =
  match deleteStep document with
  | DeleteRuns -> Loaded document, None
  | NoDeleteDialog
  | CheckingDelete
  | DeleteBlocked
  | WaitingForSlug
  | ReadyToDelete ->
    Loaded
      { document with
          Delete = Typed.Closed
          DeleteCheck = RemovalCheck.NotChecked
          DeleteDraft = Confirm.Closed },
    None

let private revisionsAvailability (document: Document) =
  match document.State, requireSaved document.Save with
  | Api.Draft, _ -> Unavailable "Only for published posts."
  | _, (Unavailable _ as notSaved) -> notSaved
  | _, Available when document.PendingEdit && document.ETag.IsNone -> Unavailable "Reload the page, then try again."
  | _, Available -> Available

let private revisionsRequested (document: Document) =
  match revisionsAvailability document with
  | Available -> Loaded { document with Revisions = Revisions.Loading }, Some(LoadRevisions document.Slug)
  | Unavailable _ -> Loaded document, None

let private revisionsAnswered (result: Result<Api.Revision list, Api.Failure>) (document: Document) =
  match document.Revisions, result with
  | Revisions.Loading, Ok revisions -> Loaded { document with Revisions = Revisions.Listed revisions }, None
  | Revisions.Loading, Error failure -> Loaded { document with Revisions = Revisions.LoadFailed(Api.reason failure) }, None
  | _ -> Loaded document, None

let private restoreChosen (revision: string) (document: Document) =
  match document.Revisions with
  | Revisions.Listed revisions
  | Revisions.RestoreFailed(revisions, _) when revisions |> List.exists (fun listed -> listed.Name = revision) ->
    Loaded { document with Revisions = Revisions.Restoring(revisions, revision) },
    Some(
      RestoreRevision
        { Slug = document.Slug
          ETag = document.ETag
          Revision = revision }
    )
  | _ -> Loaded document, None

let private restoreAnswered (result: Result<Api.RestoredRevision, Api.Failure>) (document: Document) =
  match document.Revisions, result with
  | Revisions.Restoring _, Ok _ -> Loading(document.Slug, Deploy.nextChain document.Deploy), Some(Reload document.Slug)
  | Revisions.Restoring(revisions, _), Error failure ->
    Loaded { document with Revisions = Revisions.RestoreFailed(revisions, Api.reason failure) }, None
  | _ -> Loaded document, None

let private revisionsClosed (document: Document) =
  if Revisions.isRestoring document.Revisions then
    Loaded document, None
  else
    Loaded { document with Revisions = Revisions.Closed }, None

let private sendAvailability (document: Document) =
  match document.DeliveryStarted, document.State, document.Deploy.Status with
  | Some started, _, _ -> Unavailable $"Already sent on {Dates.dateText started}."
  | None, Api.Draft, _ -> Unavailable "Only for published posts."
  | None, Api.Upcoming, _ -> Unavailable "Available after the publish date."
  | None, Api.Published, Deploy.Known Api.DeployState.Deployed -> Available
  | None, Api.Published, Deploy.Known Api.DeployState.Deploying ->
    Unavailable "Available once the latest change is live."
  | None, Api.Published, Deploy.Known Api.DeployState.DeployFailed ->
    Unavailable "The latest deploy failed. Rebuild the site first."
  | None, Api.Published, Deploy.Unread -> Unavailable "Checking the deploy status…"
  | None, Api.Published, Deploy.Unreadable _ -> Unavailable "Couldn't check the deploy status. Reload the page."

let private sendRequested (document: Document) =
  match sendAvailability document with
  | Available -> Loaded { document with SendToSubscribers = Typed.Typing ""; QueuedCount = None }, None
  | Unavailable _ -> Loaded document, None

let private introTyped (typed: string) (document: Document) =
  match document.SendToSubscribers with
  | Typed.Typing _
  | Typed.Failed _ -> Loaded { document with SendToSubscribers = Typed.Typing typed }, None
  | Typed.Closed
  | Typed.Sending _ -> Loaded document, None

let private sendConfirmed (document: Document) =
  match document.SendToSubscribers with
  | Typed.Typing intro
  | Typed.Failed(intro, _) ->
    Loaded { document with SendToSubscribers = Typed.Sending intro },
    Some(StartDelivery { Slug = document.Slug; Intro = intro })
  | Typed.Closed
  | Typed.Sending _ -> Loaded document, None

let private sendAnswered (result: Result<Api.StartedDelivery, Api.Failure>) (document: Document) =
  match document.SendToSubscribers, result with
  | Typed.Sending _, Ok started ->
    Loaded
      { document with
          SendToSubscribers = Typed.Closed
          QueuedCount = Some started.Queued
          DeliveryStarted = Some started.StartedDate },
    None
  | Typed.Sending intro, Error failure ->
    Loaded { document with SendToSubscribers = Typed.Failed(intro, Api.reason failure) }, None
  | _ -> Loaded document, None

let private sendClosed (document: Document) =
  if Typed.isSending document.SendToSubscribers then
    Loaded document, None
  else
    Loaded { document with SendToSubscribers = Typed.Closed }, None

let private changesOpened (document: Document) =
  match document.State, document.Published with
  | (Api.Published | Api.Upcoming), Some _ -> Loaded { document with Changes = ChangesShown }, None
  | _ -> Loaded document, None

let update (msg: Msg) (model: Model) : Model * Effect option =
  match model, msg with
  | Loading(slug, chain), Answered(answered, Ok post) when answered = slug -> loaded chain post, deployRead post
  | Loading(slug, _), Answered(answered, Error failure) when answered = slug -> NotLoaded(Api.reason failure), None
  | Loaded document, TitleChanged title -> edit (fun fields -> { fields with Title = title }) document
  | Loaded document, ExcerptChanged excerpt -> edit (fun fields -> { fields with Excerpt = excerpt }) document
  | Loaded document, BodyChanged body -> edit (fun fields -> { fields with Body = body }) document
  | Loaded document, SaveRequested -> saveNow document
  | Loaded document, SaveAnswered(slug, version, result) when slug = document.Slug ->
    answered version result document |> resumePublish |> resumeSlugChange
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
  | Loaded document, ChangesOpened -> changesOpened document
  | Loaded document, ChangesClosed -> Loaded { document with Changes = ChangesHidden }, None
  | Loaded document, DiscardRequested -> discardRequested document
  | Loaded document, DiscardConfirmed -> discardConfirmed document
  | Loaded document, DiscardAnswered(slug, result) when slug = document.Slug -> discardAnswered result document
  | Loaded document, DiscardClosed -> discardClosed document
  | Loaded document, UnpublishRequested -> unpublishRequested document
  | Loaded document, UnpublishConfirmed -> unpublishConfirmed document
  | Loaded document, UnpublishAnswered(slug, result) when slug = document.Slug -> unpublishAnswered result document
  | Loaded document, UnpublishClosed -> unpublishClosed document
  | Loaded document, ChangeDateRequested -> changeDateRequested document
  | Loaded document, ChangeDateTyped typed -> changeDateTyped typed document
  | Loaded document, ChangeDateConfirmed -> changeDateConfirmed document
  | Loaded document, ChangeDateAnswered(slug, result) when slug = document.Slug -> changeDateAnswered result document
  | Loaded document, ChangeDateClosed -> changeDateClosed document
  | Loaded document, ChangeNoticeDismissed -> Loaded { document with ChangeWarnings = [] }, None
  | Loaded document, DeleteRequested -> deleteRequested document
  | Loaded document, DeleteTyped typed -> deleteTyped typed document
  | Loaded document, RemovalCheckAnswered(slug, dialog, result) when slug = document.Slug -> removalCheckAnswered dialog result document
  | Loaded document, DeleteConfirmed -> deleteConfirmed document
  | Loaded document, DeleteAnswered(slug, result) when slug = document.Slug -> deleteAnswered result document
  | Loaded document, DeleteClosed -> deleteClosed document
  | Loaded document, RevisionsRequested -> revisionsRequested document
  | Loaded document, RevisionsAnswered(slug, result) when slug = document.Slug -> revisionsAnswered result document
  | Loaded document, RestoreChosen revision -> restoreChosen revision document
  | Loaded document, RestoreAnswered(slug, result) when slug = document.Slug -> restoreAnswered result document
  | Loaded document, RevisionsClosed -> revisionsClosed document
  | Loaded document, SendRequested -> sendRequested document
  | Loaded document, IntroTyped typed -> introTyped typed document
  | Loaded document, SendConfirmed -> sendConfirmed document
  | Loaded document, SendAnswered(slug, result) when slug = document.Slug -> sendAnswered result document
  | Loaded document, SendClosed -> sendClosed document
  | Loaded document, SentNoticeDismissed -> Loaded { document with QueuedCount = None }, None
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
  | Loaded { Save = Stale } -> "This post changed in another tab. Copy your text, then reload the page."
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

let primaryAvailability (document: Document) =
  match document.State, document.Save with
  | _ when canPublish document -> Available
  | Api.Draft, Stale -> Unavailable "This post changed in another tab. Reload the page to get the latest version."
  | Api.Draft, _ -> Unavailable "Wait until the slug change ends."
  | (Api.Published | Api.Upcoming), Stale -> Unavailable "This post changed in another tab. Reload the page to get the latest version."
  | (Api.Published | Api.Upcoming), _ -> Unavailable "The post has no changes to publish."

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
  | Saved -> Unavailable "No unsaved changes."
  | Saving _ -> Unavailable "Saving…"
  | Stale -> Unavailable "This post changed in another tab. Reload the page to get the latest version."

let private availability (document: Document) (action: MenuAction) =
  match action, document.State with
  | SaveNow, _ -> saveAvailability document
  | ChangesFromLive, Api.Draft -> Unavailable "Only for published posts."
  | ChangesFromLive, (Api.Published | Api.Upcoming) when document.Published.IsNone ->
    Unavailable "Reload the page, then try again."
  | ChangesFromLive, (Api.Published | Api.Upcoming) -> Available
  | DiscardChanges, _ -> discardAvailability document
  | ChangeDate, (Api.Draft | Api.Published) -> Unavailable "Only for upcoming posts."
  | Unpublish, (Api.Draft | Api.Published) -> Unavailable "Only for upcoming posts."
  | Revisions, _ -> revisionsAvailability document
  | SendToSubscribers, _ -> sendAvailability document
  | Delete, _ -> deleteAvailability document
  | (ChangeDate | Unpublish), Api.Upcoming -> upcomingChangeAvailability document

let menu (document: Document) : MenuItem list =
  [ SaveNow, "Save"
    ChangesFromLive, "Compare with live"
    DiscardChanges, "Discard changes"
    ChangeDate, "Change date"
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
  | ChangesFromLive -> Some ChangesOpened
  | DiscardChanges -> Some DiscardRequested
  | ChangeDate -> Some ChangeDateRequested
  | Unpublish -> Some UnpublishRequested
  | Revisions -> Some RevisionsRequested
  | SendToSubscribers -> Some SendRequested
  | Delete -> Some DeleteRequested

let queuedText (queued: int) =
  match queued with
  | 0 -> "No active subscribers. No emails sent."
  | 1 -> "Queued 1 email."
  | count -> $"Queued {count} emails."

let private detailsText (details: Api.PostDetails) =
  let value = Option.defaultValue ""
  let tags = String.concat ", " details.Tags

  [ $"author: {value details.Author}"
    $"category: {value details.Category}"
    $"tags: {tags}"
    $"series: {value details.Series}"
    $"image: {value details.Image}"
    $"showOnHomepage: {details.ShowOnHomepage}" ]
  |> String.concat "\n"

type ChangedPart = { Name: string; Entries: Diff.Entry list }

let changesFromLive (document: Document) : ChangedPart list =
  match document.Published with
  | None -> []
  | Some published ->
    [ "Title", published.Title, document.Fields.Title
      "Excerpt", published.Excerpt, document.Fields.Excerpt
      "Body", published.Body, document.Fields.Body
      "Details", detailsText published.Details, detailsText document.Details.Values ]
    |> List.choose (fun (name, before, after) ->
      let lines = Diff.lines before after

      if Diff.hasChanges lines then
        Some
          { Name = name
            Entries = Diff.withContext 3 lines }
      else
        None)
