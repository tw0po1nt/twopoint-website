namespace TwoPoint.Core.Posts.Actions

open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

open System

[<RequireQualifiedAccess>]
module PendingGate =

  type Refusal =
    | PostNotFound of Slug
    | NotPublished of Slug
    | NoPendingEdit of Slug
    | WorkingBlobChanged
    | PublishedBlobChanged
    | NoPublishDate of Slug
    | FrontMatterUnreadable of reason: string

  /// `IfMatch` is the ETag of the working blob that the editor read. `None` for `UpdateDate` leaves the update date as it is.
  type Dto =
    { Slug: string
      IfMatch: string option
      UpdateDate: string option }

  type Gated =
    { Slug: Slug
      PublishDate: DateTime
      PublishedAt: DateTime
      Text: string
      Published: StoredBlob
      Working: StoredBlob
      Problems: TwoPoint.Site.Render.Problem list }

  [<RequireQualifiedAccess>]
  module Logic =

    let pending
      (slug: Slug)
      (published: StoredBlob option)
      (working: StoredBlob option)
      (ifMatch: string option)
      : Decision<StoredBlob * StoredBlob, Refusal> =
      match published, working with
      | None, None -> Decision.failure (PostNotFound slug)
      | None, Some _ -> Decision.failure (NotPublished slug)
      | Some _, None -> Decision.failure (NoPendingEdit slug)
      | Some _, Some blob when ifMatch <> Some blob.ETag -> Decision.failure WorkingBlobChanged
      | Some published, Some blob -> Decision.success (published, blob)

    let publishDate (slug: Slug) (publishedText: string) : Decision<DateTimeOffset, Refusal> =
      match (TwoPoint.Site.Render.Post.summary publishedText).PublishDate with
      | Some date -> Decision.success date
      | None -> Decision.failure (NoPublishDate slug)

  let private evaluate
    (dependencies: PublishGate.Dependencies)
    (slug: Slug)
    (ifMatch: string option)
    (updateDate: DateTime option)
    : CancellableTask<ActionResult<Gated, Refusal>> =
    cancellableTaskResult {
      let now = dependencies.GetUtcNow()
      let! (loaded: PublishGate.Loaded) = PublishGate.load dependencies slug |> CancellableTaskResult.mapError ActionError<Refusal>.Dependency

      let! (published: StoredBlob), (working: StoredBlob) =
        Logic.pending slug loaded.Published loaded.Working ifMatch |> Result.mapError ActionError<Refusal>.Logic

      let! publishDate = Logic.publishDate slug published.Text |> Result.mapError ActionError<Refusal>.Logic

      let! dated =
        TwoPoint.Site.Render.PostText.withPublishDate working.Text publishDate
        |> Result.mapError (FrontMatterUnreadable >> ActionError<Refusal>.Logic)

      let! text =
        match updateDate with
        | None -> Ok dated
        | Some date -> TwoPoint.Site.Render.PostText.withUpdateDate dated (DateTimeOffset date)
        |> Result.mapError (FrontMatterUnreadable >> ActionError<Refusal>.Logic)

      let series, parsed = dependencies.ParsePublished loaded.Posts loaded.Series

      let checkedPost =
        TwoPoint.Site.Render.Check.forPublish dependencies.Stage dependencies.Images series parsed (Slug.value slug) text

      return
        { Slug = slug
          PublishDate = publishDate.UtcDateTime
          PublishedAt = PublishTime.whole now
          Text = text
          Published = published
          Working = working
          Problems = checkedPost.Problems }
    }

  let prepare (dependencies: PublishGate.Dependencies) (dto: Dto) : CancellableTask<ActionResult<Gated, Refusal>> =
    cancellableTaskResult {
      let! slug = dto.Slug |> Slug.create |> Result.mapError ActionError<Refusal>.Validation

      let! updateDate =
        Option.traverseResult (PublishTime.parseUtc "updateDate") dto.UpdateDate
        |> Result.mapError ActionError<Refusal>.Validation

      return! evaluate dependencies slug dto.IfMatch updateDate
    }

[<RequireQualifiedAccess>]
module CheckChanges =

  type Dto = PendingGate.Dto

  type Error = Refused of PendingGate.Refusal

  type Event = Checked of TwoPoint.Site.Render.Problem list

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  let withDependencies (dependencies: PublishGate.Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! (gated: PendingGate.Gated) =
        PendingGate.prepare dependencies dto
        |> CancellableTask.map (Result.mapError (ActionError.mapLogic Refused))

      return Checked gated.Problems
    }

[<RequireQualifiedAccess>]
module PublishChanges =

  type Dto = PendingGate.Dto

  type Error =
    | Refused of PendingGate.Refusal
    | HasErrors of TwoPoint.Site.Render.Problem list
    | RevisionExists of Slug

  type Event = Published of PublishSteps.Outcome

  type Dependencies = PublishSteps.GatedDependencies

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  let withDependencies (dependencies: Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! (gated: PendingGate.Gated) =
        PendingGate.prepare dependencies dto
        |> CancellableTask.map (Result.mapError (ActionError.mapLogic Refused))

      let errors = gated.Problems |> List.filter TwoPoint.Site.Render.Problem.isError

      do!
        match errors with
        | [] -> Ok()
        | _ -> Result.Error(ActionError.Logic(HasErrors errors))

      let! post =
        PublishSteps.row gated.Slug gated.PublishDate gated.Text
        |> Result.mapError ActionError<Error>.Validation

      do!
        PublishSteps.writeRevisionAndPublished
          dependencies
          gated.Slug
          gated.PublishedAt
          gated.Text
          gated.Published.Mark
          (MatchesETag gated.Published.ETag)
        |> CancellableTask.map (
          Result.mapError (function
            | PublishSteps.RevisionTaken -> ActionError.Logic(RevisionExists gated.Slug)
            | PublishSteps.PublishedConditionFailed -> ActionError.Logic(Refused PendingGate.PublishedBlobChanged)
            | PublishSteps.WriteFailed error -> ActionError<Error>.Dependency error)
        )

      let! outcome = PublishSteps.complete dependencies post gated.Working.ETag gated.PublishedAt
      return Published outcome
    }

[<RequireQualifiedAccess>]
module DiscardChanges =

  /// `IfMatch` is the ETag of the working blob that the editor read.
  type Dto = { Slug: string; IfMatch: string option }

  type Error = Refused of PendingGate.Refusal

  type Event = Discarded

  type Dependencies =
    abstract member GetPublishedBlob: slug: Slug -> DependencyResult<StoredBlob option>
    abstract member GetWorkingBlob: slug: Slug -> DependencyResult<StoredBlob option>
    abstract member DeleteWorkingBlob: slug: Slug -> eTag: string -> DependencyResult<DeleteOutcome>

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  let withDependencies (dependencies: Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! slug = dto.Slug |> Slug.create |> Result.mapError ActionError<Error>.Validation

      let! published, working =
        CancellableTaskResult.parallelZip (dependencies.GetPublishedBlob slug) (dependencies.GetWorkingBlob slug)
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! (_: StoredBlob), (working: StoredBlob) =
        PendingGate.Logic.pending slug published working dto.IfMatch
        |> Result.mapError (Refused >> ActionError.Logic)

      let! deleted =
        dependencies.DeleteWorkingBlob slug working.ETag
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      match deleted with
      | Deleted -> return Discarded
      | DeleteConditionFailed -> return! Result.Error(ActionError.Logic(Refused PendingGate.WorkingBlobChanged))
    }

[<RequireQualifiedAccess>]
module RestoreRevision =

  /// `IfMatch` is the ETag of the working blob that the editor read. `None` means that the editor read no working blob.
  type Dto =
    { Slug: string
      Revision: string
      IfMatch: string option }

  type Error =
    | PostNotFound of Slug
    | NotPublished of Slug
    | RevisionNotFound of Slug * RevisionName
    /// The working blob changed after the editor read it, or the editor read a blob that no longer exists.
    | Stale

  type Event = Restored of eTag: string

  [<RequireQualifiedAccess>]
  module Logic =

    let condition
      (slug: Slug)
      (published: StoredBlob option)
      (working: StoredBlob option)
      (ifMatch: string option)
      : Decision<WriteCondition, Error> =
      match published, working, ifMatch with
      | None, None, _ -> Decision.failure (PostNotFound slug)
      | None, Some _, _ -> Decision.failure (NotPublished slug)
      | Some _, Some blob, Some eTag when blob.ETag = eTag -> Decision.success (MatchesETag eTag)
      | Some _, Some _, _
      | Some _, None, Some _ -> Decision.failure Stale
      | Some _, None, None -> Decision.success MustNotExist

  type Dependencies =
    abstract member GetPublishedBlob: slug: Slug -> DependencyResult<StoredBlob option>
    abstract member GetWorkingBlob: slug: Slug -> DependencyResult<StoredBlob option>
    abstract member GetRevisionBlob: slug: Slug -> name: RevisionName -> DependencyResult<string option>
    abstract member WriteWorkingBlob: slug: Slug -> text: string -> condition: WriteCondition -> DependencyResult<WriteOutcome>

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  let withDependencies (dependencies: Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! slug = dto.Slug |> Slug.create |> Result.mapError ActionError<Error>.Validation
      let! name = dto.Revision |> RevisionName.create |> Result.mapError ActionError<Error>.Validation

      let! published, working =
        CancellableTaskResult.parallelZip (dependencies.GetPublishedBlob slug) (dependencies.GetWorkingBlob slug)
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! condition =
        Logic.condition slug published working dto.IfMatch
        |> Result.mapError ActionError<Error>.Logic

      let! revision =
        dependencies.GetRevisionBlob slug name
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      match revision with
      | None -> return! Result.Error(ActionError.Logic(RevisionNotFound(slug, name)))
      | Some text ->
        let! outcome =
          dependencies.WriteWorkingBlob slug text condition
          |> CancellableTaskResult.mapError ActionError<Error>.Dependency

        match outcome with
        | Written eTag -> return Restored eTag
        | ConditionFailed -> return! Result.Error(ActionError.Logic Stale)
    }
