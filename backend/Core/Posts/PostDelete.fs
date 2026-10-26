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
  type Dto =
    { Slug: string
      IfMatch: string option
      ConfirmSlug: string option }

  type Error =
    | PostNotFound of Slug
    | WorkingBlobChanged
    | PublishedBlobChanged
    | SlugNotConfirmed of Slug
    | BreaksLinks of TwoPoint.Site.Render.Problem list

  type Outcome =
    | DraftDeleted of warnings: ChangeWarning list
    | PublishedDeleted of production: ProductionDispatch * warnings: ChangeWarning list

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

  let private finishPublished (dependencies: Dependencies) (slug: Slug) (earlier: ChangeWarning list) : CancellableTask<Outcome> =
    cancellableTask {
      let! records = removeRecords dependencies slug
      let! rebuilt = PublishSteps.rebuild dependencies slug
      return PublishedDeleted(rebuilt.Production, earlier @ records @ List.map Step rebuilt.Warnings)
    }

  let private deletePublished
    (dependencies: Dependencies)
    (slug: Slug)
    (published: StoredBlob)
    (working: StoredBlob option)
    : CancellableTask<ActionResult<Outcome, Error>> =
    cancellableTaskResult {
      let! deleted =
        dependencies.DeletePublishedBlob slug published.ETag
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      do!
        match deleted with
        | Deleted -> Ok()
        | DeleteConditionFailed -> Result.Error(ActionError.Logic PublishedBlobChanged)

      let! workingWarnings =
        match working with
        | Some blob -> dependencies.DeleteWorkingBlob slug blob.ETag |> CancellableTask.map Logic.workingWarnings
        | None -> CancellableTask.singleton []

      return! finishPublished dependencies slug workingWarnings |> CancellableTask.map Ok
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
        | CleanUp -> finishPublished dependencies slug [] |> CancellableTask.map Ok
        | DeletePublished(published, working) -> cancellableTaskResult {
            let _, parsed = dependencies.ParsePublished loaded.Posts loaded.Series

            let errors =
              TwoPoint.Site.Render.Check.withoutPost dependencies.Stage parsed (Slug.value slug)
              |> List.filter TwoPoint.Site.Render.Problem.isError

            do!
              match errors with
              | [] -> Ok()
              | _ -> Result.Error(ActionError.Logic(BreaksLinks errors))

            return! deletePublished dependencies slug published working
          }

      return PostDeleted outcome
    }
