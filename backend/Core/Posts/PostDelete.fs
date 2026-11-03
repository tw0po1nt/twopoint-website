namespace TwoPoint.Core.Posts.Actions

open TwoPoint.Core.Deploys
open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

[<RequireQualifiedAccess>]
module DeletePost =

  /// `IfMatch` is `None` when the post has no working blob. `ConfirmSlug` must equal the slug of a published post.
  /// `Edits` are the pending edits of the removal check. An empty list is a plain removal.
  type Dto =
    { Slug: string
      IfMatch: string option
      ConfirmSlug: string option
      Edits: RemovalSteps.EditRef list }

  type Error =
    | PostNotFound of Slug
    | WorkingBlobChanged
    | PublishedBlobChanged
    | SlugNotConfirmed of Slug
    | BreaksLinks of TwoPoint.Site.Render.Problem list
    /// The pending edits of the request are not the pending edits of the removal check.
    | EditsChanged

  /// Why the delete kept the post after it published one or more pending edits.
  type KeptReason =
    | EditNotPublished of Slug * PublishSteps.WriteFailure
    | PublishedBlobKept
    | PublishedDeleteFailed of DependencyError

  type Outcome =
    | DraftDeleted of warnings: ChangeWarning list
    /// `edits` are the posts whose pending edits the delete published first.
    | PublishedDeleted of edits: Slug list * production: ProductionDispatch * warnings: ChangeWarning list
    /// The delete published the pending edits of `edits`, and a step after that failed. The post stays.
    | PostKept of edits: Slug list * reason: KeptReason * production: ProductionDispatch * warnings: PublishSteps.Warning list

  type Event = PostDeleted of Outcome

  type Dependencies =
    inherit PublishSteps.GatedDependencies
    abstract member DeletePublishedBlob: slug: Slug -> eTag: string -> DependencyResult<DeleteOutcome>
    abstract member DeleteRevisionBlobs: slug: Slug -> DependencyResult<unit>
    abstract member DeletePostComments: slug: Slug -> DependencyResult<unit>
    abstract member DeletePostRow: slug: Slug -> DependencyResult<unit>
    abstract member HasPostRecords: slug: Slug -> DependencyResult<bool>

  type Plan =
    | DeleteDraft of working: StoredBlob
    | DeletePublished of published: StoredBlob * working: StoredBlob option
    | CleanUp

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  [<RequireQualifiedAccess>]
  module Logic =

    let decide
      (slug: Slug)
      (published: StoredBlob option)
      (working: StoredBlob option)
      (ifMatch: string option)
      (confirmSlug: string option)
      (hasRecords: bool)
      : Decision<Plan, Error> =
      match published, working with
      | _ when not (PublishGate.Logic.workingMatches working ifMatch) -> Decision.failure WorkingBlobChanged
      | None, None when hasRecords -> Decision.success CleanUp
      | None, None -> Decision.failure (PostNotFound slug)
      | None, Some blob -> Decision.success (DeleteDraft blob)
      | Some _, _ when confirmSlug <> Some(Slug.value slug) -> Decision.failure (SlugNotConfirmed slug)
      | Some published, working -> Decision.success (DeletePublished(published, working))

    let refusal (refusal: RemovalSteps.Refusal) : Error =
      match refusal with
      | RemovalSteps.BreaksLinks problems -> BreaksLinks problems
      | RemovalSteps.EditsChanged -> EditsChanged

    let workingWarnings (deleted: Result<DeleteOutcome, DependencyError>) : ChangeWarning list =
      match deleted with
      | Error error -> [ Step(PublishSteps.PendingEditKept error) ]
      | Ok DeleteConditionFailed -> [ Step PublishSteps.NewerTextKept ]
      | Ok Deleted -> []

    let recordWarnings
      (revisions: Result<unit, DependencyError>)
      (row: Result<unit, DependencyError>)
      (comments: Result<unit, DependencyError>)
      : ChangeWarning list =
      [ match revisions with
        | Error error -> RevisionsKept error
        | Ok() -> ()

        match row with
        | Error error -> PostRowKept error
        | Ok() -> ()

        match comments with
        | Error error -> CommentsKept error
        | Ok() -> () ]

  let private removeRecords (dependencies: Dependencies) (slug: Slug) : CancellableTask<ChangeWarning list> =
    cancellableTask {
      let! revisions = dependencies.DeleteRevisionBlobs slug
      let! row = PublishSteps.retryOnce (fun () -> dependencies.DeletePostRow slug)
      let! comments = dependencies.DeletePostComments slug
      return Logic.recordWarnings revisions row comments
    }

  let private finishPublished
    (dependencies: Dependencies)
    (slug: Slug)
    (edits: Slug list)
    (earlier: ChangeWarning list)
    : CancellableTask<Outcome> =
    cancellableTask {
      let! records = removeRecords dependencies slug
      let! rebuilt = PublishSteps.rebuildAll dependencies (edits @ [ slug ])
      return PublishedDeleted(edits, rebuilt.Production, earlier @ records @ List.map Step rebuilt.Warnings)
    }

  let private kept (reason: KeptReason) (kept: RemovalSteps.Kept) =
    PostKept(kept.Edits, reason, kept.Production, kept.Warnings)

  let private deletePublished
    (dependencies: Dependencies)
    (slug: Slug)
    (published: StoredBlob)
    (working: StoredBlob option)
    (edits: RemovalSteps.GatedEdit list)
    : CancellableTask<ActionResult<Outcome, Error>> =
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
        match! dependencies.DeletePublishedBlob slug published.ETag with
        | Error error -> return! fail (ActionError.Dependency error) (PublishedDeleteFailed error)
        | Ok DeleteConditionFailed -> return! fail (ActionError.Logic PublishedBlobChanged) PublishedBlobKept
        | Ok Deleted ->
          let! workingWarnings =
            match working with
            | Some blob -> dependencies.DeleteWorkingBlob slug blob.ETag |> CancellableTask.map Logic.workingWarnings
            | None -> CancellableTask.singleton []

          let! outcome = finishPublished dependencies slug publishedEdits.Published (List.map Step publishedEdits.Warnings @ workingWarnings)
          return Ok outcome
    }

  let private deleteDraft (dependencies: Dependencies) (slug: Slug) (working: StoredBlob) : CancellableTask<ActionResult<Outcome, Error>> =
    cancellableTaskResult {
      let! deleted =
        dependencies.DeleteWorkingBlob slug working.ETag
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      do!
        match deleted with
        | Deleted -> Ok()
        | DeleteConditionFailed -> Result.Error(ActionError.Logic WorkingBlobChanged)

      let! records = removeRecords dependencies slug
      return DraftDeleted records
    }

  let withDependencies (dependencies: Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! slug = dto.Slug |> Slug.create |> Result.mapError ActionError<Error>.Validation

      let! (loaded: PublishGate.Loaded) =
        PublishGate.load dependencies slug |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! hasRecords =
        match loaded.Published, loaded.Working with
        | None, None -> dependencies.HasPostRecords slug |> CancellableTaskResult.mapError ActionError<Error>.Dependency
        | _ -> CancellableTaskResult.singleton false

      let! plan =
        Logic.decide slug loaded.Published loaded.Working dto.IfMatch dto.ConfirmSlug hasRecords
        |> Result.mapError ActionError<Error>.Logic

      let! outcome =
        match plan with
        | DeleteDraft working -> deleteDraft dependencies slug working
        | CleanUp -> finishPublished dependencies slug [] [] |> CancellableTask.map Ok
        | DeletePublished(published, working) -> cancellableTaskResult {
            let! edits =
              RemovalSteps.prepare dependencies slug loaded dto.Edits
              |> CancellableTask.map (Result.mapError (ActionError.mapLogic Logic.refusal))

            return! deletePublished dependencies slug published working edits
          }

      return PostDeleted outcome
    }
