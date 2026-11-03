namespace TwoPoint.Core.Posts.Actions

open TwoPoint.Core.Deploys
open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

open System

type ChangeWarning =
  | Step of PublishSteps.Warning
  | PostRowKept of DependencyError
  | RevisionsKept of DependencyError
  | CommentsKept of DependencyError
  | PendingDateNotUpdated of DependencyError
  | PendingDateUnreadable of reason: string

/// `WorkingETag` is the ETag of the working blob after the change.
type UpcomingChange =
  { PublishDate: DateTime option
    State: PostState
    WorkingETag: string option
    Production: ProductionDispatch
    Warnings: ChangeWarning list }

[<RequireQualifiedAccess>]
module UpcomingGate =

  type Refusal =
    | Gate of PendingGate.Refusal
    | NotUpcoming of Slug

  type UpcomingPost =
    { Published: StoredBlob
      PublishDate: DateTime
      Working: StoredBlob option }

  type Dependencies =
    inherit PublishSteps.GatedDependencies
    abstract member WriteWorkingBlob: slug: Slug -> text: string -> condition: WriteCondition -> DependencyResult<WriteOutcome>

  [<RequireQualifiedAccess>]
  module Logic =

    let upcoming
      (slug: Slug)
      (now: DateTime)
      (published: StoredBlob option)
      (working: StoredBlob option)
      (ifMatch: string option)
      : Decision<UpcomingPost, Refusal> =
      match published, working with
      | None, None -> Decision.failure (Gate(PendingGate.PostNotFound slug))
      | None, Some _ -> Decision.failure (Gate(PendingGate.NotPublished slug))
      | Some blob, _ ->
        PendingGate.Logic.publishDate slug blob.Text
        |> Result.mapError Gate
        |> Result.bind (fun date ->
          if date.UtcDateTime <= now then
            Decision.failure (NotUpcoming slug)
          elif not (PublishGate.Logic.workingMatches working ifMatch) then
            Decision.failure (Gate PendingGate.WorkingBlobChanged)
          else
            Decision.success
              { Published = blob
                PublishDate = date.UtcDateTime
                Working = working })

  let load
    (dependencies: PublishGate.Dependencies)
    (slug: Slug)
    (ifMatch: string option)
    : CancellableTask<ActionResult<UpcomingPost * PublishGate.Loaded, Refusal>> =
    cancellableTaskResult {
      let now = dependencies.GetUtcNow()
      let! (loaded: PublishGate.Loaded) = PublishGate.load dependencies slug |> CancellableTaskResult.mapError ActionError<Refusal>.Dependency

      let! upcoming =
        Logic.upcoming slug now loaded.Published loaded.Working ifMatch
        |> Result.mapError ActionError<Refusal>.Logic

      return upcoming, loaded
    }

[<RequireQualifiedAccess>]
module Unpublish =

  /// `IfMatch` is the ETag of the working blob that the editor read. `None` means that the post has no pending edit.
  /// `Edits` are the pending edits of the removal check. An empty list is a plain removal.
  type Dto =
    { Slug: string
      IfMatch: string option
      Edits: RemovalSteps.EditRef list }

  type Error =
    | Refused of UpcomingGate.Refusal
    | BreaksLinks of TwoPoint.Site.Render.Problem list
    /// The pending edits of the request are not the pending edits of the removal check.
    | EditsChanged

  /// Why the unpublish kept the post upcoming after it published one or more pending edits.
  type KeptReason =
    | EditNotPublished of Slug * PublishSteps.WriteFailure
    | WorkingBlobKept
    | WorkingWriteFailed of DependencyError
    | PublishedBlobKept
    | PublishedDeleteFailed of DependencyError

  type Event =
    | Unpublished of UpcomingChange
    /// The unpublish published the pending edits of `edits`, and a step after that failed. The post stays upcoming.
    | PostKept of edits: Slug list * reason: KeptReason * production: ProductionDispatch * warnings: PublishSteps.Warning list

  type Dependencies =
    inherit UpcomingGate.Dependencies
    abstract member DeletePublishedBlob: slug: Slug -> eTag: string -> DependencyResult<DeleteOutcome>
    abstract member DeletePostRow: slug: Slug -> DependencyResult<unit>

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  [<RequireQualifiedAccess>]
  module Logic =

    let refusal (refusal: RemovalSteps.Refusal) : Error =
      match refusal with
      | RemovalSteps.BreaksLinks problems -> BreaksLinks problems
      | RemovalSteps.EditsChanged -> EditsChanged

  let private condition (working: StoredBlob option) =
    match working with
    | Some blob -> MatchesETag blob.ETag
    | None -> MustNotExist

  let private kept (reason: KeptReason) (kept: RemovalSteps.Kept) =
    PostKept(kept.Edits, reason, kept.Production, kept.Warnings)

  let private refused (gate: PendingGate.Refusal) : ActionError<Error> =
    ActionError.Logic(Refused(UpcomingGate.Gate gate))

  let private unpublish
    (dependencies: Dependencies)
    (slug: Slug)
    (upcoming: UpcomingGate.UpcomingPost)
    (draftText: string)
    (edits: RemovalSteps.GatedEdit list)
    : CancellableTask<ActionResult<Event, Error>> =
    cancellableTask {
      let! publishedEdits = RemovalSteps.publishEdits dependencies edits
      let fail refusal reason = RemovalSteps.fail dependencies publishedEdits refusal (kept reason)

      match publishedEdits.Failure with
      | Some(editSlug, failure) ->
        return!
          fail
            (RemovalSteps.Logic.firstEditError failure |> ActionError.mapLogic Logic.refusal)
            (EditNotPublished(editSlug, failure))
      | None ->
        match! dependencies.WriteWorkingBlob slug draftText (condition upcoming.Working) with
        | Error error -> return! fail (ActionError.Dependency error) (WorkingWriteFailed error)
        | Ok ConditionFailed -> return! fail (refused PendingGate.WorkingBlobChanged) WorkingBlobKept
        | Ok(Written draftETag) ->
          match! dependencies.DeletePublishedBlob slug upcoming.Published.ETag with
          | Error error -> return! fail (ActionError.Dependency error) (PublishedDeleteFailed error)
          | Ok DeleteConditionFailed -> return! fail (refused PendingGate.PublishedBlobChanged) PublishedBlobKept
          | Ok Deleted ->
            let! row = PublishSteps.retryOnce (fun () -> dependencies.DeletePostRow slug)
            let! rebuilt = PublishSteps.rebuildAll dependencies (publishedEdits.Published @ [ slug ])

            let rowWarnings =
              match row with
              | Ok() -> []
              | Error error -> [ PostRowKept error ]

            return
              Ok(
                Unpublished
                  { PublishDate = None
                    State = PostState.Draft
                    WorkingETag = Some draftETag
                    Production = rebuilt.Production
                    Warnings = List.map Step publishedEdits.Warnings @ rowWarnings @ List.map Step rebuilt.Warnings }
              )
    }

  let withDependencies (dependencies: Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! slug = dto.Slug |> Slug.create |> Result.mapError ActionError<Error>.Validation

      let! (upcoming: UpcomingGate.UpcomingPost), (loaded: PublishGate.Loaded) =
        UpcomingGate.load dependencies slug dto.IfMatch
        |> CancellableTask.map (Result.mapError (ActionError.mapLogic Refused))

      let source =
        match upcoming.Working with
        | Some working -> working.Text
        | None -> upcoming.Published.Text

      let! draftText =
        TwoPoint.Site.Render.PostText.withoutPublishDate source
        |> Result.mapError (PendingGate.FrontMatterUnreadable >> UpcomingGate.Gate >> Refused >> ActionError<Error>.Logic)

      let! edits =
        RemovalSteps.prepare dependencies slug loaded dto.Edits
        |> CancellableTask.map (Result.mapError (ActionError.mapLogic Logic.refusal))

      return! unpublish dependencies slug upcoming draftText edits
    }

[<RequireQualifiedAccess>]
module ChangeDate =

  /// `IfMatch` is the ETag of the working blob that the editor read. `None` means that the post has no pending edit.
  type Dto =
    { Slug: string
      IfMatch: string option
      PublishDate: string }

  type Error = Refused of UpcomingGate.Refusal

  type Event = DateChanged of UpcomingChange

  type Dependencies = UpcomingGate.Dependencies

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  let private redatePending
    (dependencies: Dependencies)
    (slug: Slug)
    (pending: StoredBlob)
    (publishDate: DateTime)
    : CancellableTask<string option * ChangeWarning list> =
    cancellableTask {
      match TwoPoint.Site.Render.PostText.withPublishDate pending.Text (DateTimeOffset publishDate) with
      | Result.Error reason -> return Some pending.ETag, [ PendingDateUnreadable reason ]
      | Ok text ->
        match! dependencies.WriteWorkingBlob slug text (MatchesETag pending.ETag) with
        | Ok(Written eTag) -> return Some eTag, []
        | Ok ConditionFailed -> return None, [ Step PublishSteps.NewerTextKept ]
        | Error error -> return Some pending.ETag, [ PendingDateNotUpdated error ]
    }

  let withDependencies (dependencies: Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! slug = dto.Slug |> Slug.create |> Result.mapError ActionError<Error>.Validation

      let! date =
        PublishTime.parseUtc "publishDate" dto.PublishDate
        |> Result.mapError ActionError<Error>.Validation

      let publishedAt = PublishTime.whole (dependencies.GetUtcNow())

      let! (upcoming: UpcomingGate.UpcomingPost), (_: PublishGate.Loaded) =
        UpcomingGate.load dependencies slug dto.IfMatch
        |> CancellableTask.map (Result.mapError (ActionError.mapLogic Refused))

      let! datedText =
        TwoPoint.Site.Render.PostText.withPublishDate upcoming.Published.Text (DateTimeOffset date)
        |> Result.mapError (PendingGate.FrontMatterUnreadable >> UpcomingGate.Gate >> Refused >> ActionError<Error>.Logic)

      let! post = PublishSteps.row slug date datedText |> Result.mapError ActionError<Error>.Validation

      let! written =
        dependencies.WritePublishedBlob slug datedText upcoming.Published.Mark (MatchesETag upcoming.Published.ETag)
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      do!
        match written with
        | Written _ -> Ok()
        | ConditionFailed -> Result.Error(ActionError.Logic(Refused(UpcomingGate.Gate PendingGate.PublishedBlobChanged)))

      let! row = PublishSteps.retryOnce (fun () -> dependencies.UpsertPostRow post)

      let! workingETag, pendingWarnings =
        match upcoming.Working with
        | Some pending -> redatePending dependencies slug pending date
        | None -> CancellableTask.singleton (None, [])

      let! rebuilt = PublishSteps.rebuild dependencies slug

      let rowWarnings =
        match row with
        | Ok() -> []
        | Error error -> [ Step(PublishSteps.CommentRowNotWritten error) ]

      return
        DateChanged
          { PublishDate = Some date
            State = PublishSteps.Logic.state publishedAt date
            WorkingETag = workingETag
            Production = rebuilt.Production
            Warnings = rowWarnings @ pendingWarnings @ List.map Step rebuilt.Warnings }
    }
