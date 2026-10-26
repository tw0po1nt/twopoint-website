namespace TwoPoint.Core.Posts.Actions

open TwoPoint.Core.Deploys
open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

open System

[<RequireQualifiedAccess>]
module UpcomingGate =

  type Refusal =
    | PostNotFound of Slug
    | NotPublished of Slug
    | NotUpcoming of Slug
    | NoPublishDate of Slug
    | WorkingBlobChanged
    | PublishedBlobChanged
    | FrontMatterUnreadable of reason: string

  type UpcomingPost =
    { Published: StoredBlob
      PublishDate: DateTime
      Working: StoredBlob option }

  [<RequireQualifiedAccess>]
  module Logic =

    let private workingMatches (working: StoredBlob option) (ifMatch: string option) =
      match working, ifMatch with
      | Some blob, Some eTag -> blob.ETag = eTag
      | None, None -> true
      | Some _, None
      | None, Some _ -> false

    let upcoming
      (slug: Slug)
      (now: DateTime)
      (published: StoredBlob option)
      (working: StoredBlob option)
      (ifMatch: string option)
      : Decision<UpcomingPost, Refusal> =
      match published, working with
      | None, None -> Decision.failure (PostNotFound slug)
      | None, Some _ -> Decision.failure (NotPublished slug)
      | Some blob, _ ->
        match (TwoPoint.Site.Render.Post.summary blob.Text).PublishDate with
        | None -> Decision.failure (NoPublishDate slug)
        | Some date when date.UtcDateTime <= now -> Decision.failure (NotUpcoming slug)
        | Some _ when not (workingMatches working ifMatch) -> Decision.failure WorkingBlobChanged
        | Some date ->
          Decision.success
            { Published = blob
              PublishDate = date.UtcDateTime
              Working = working }

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

/// `WorkingETag` is the ETag of the working blob after the change.
type Moved =
  { PublishDate: DateTime option
    State: PostState
    WorkingETag: string option
    Production: ProductionDispatch
    Warnings: PublishSteps.Warning list }

[<RequireQualifiedAccess>]
module Unpublish =

  /// `IfMatch` is the ETag of the working blob that the editor read. `None` means that the post has no pending edit.
  type Dto = { Slug: string; IfMatch: string option }

  type Error =
    | Refused of UpcomingGate.Refusal
    | HasErrors of TwoPoint.Site.Render.Problem list

  type Event = Unpublished of Moved

  type Dependencies =
    inherit PublishSteps.GatedDependencies
    abstract member WriteWorkingBlob: slug: Slug -> text: string -> condition: WriteCondition -> DependencyResult<WriteOutcome>
    abstract member DeletePublishedBlob: slug: Slug -> eTag: string -> DependencyResult<DeleteOutcome>
    abstract member DeletePostRow: slug: Slug -> DependencyResult<unit>

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  let private condition (working: StoredBlob option) =
    match working with
    | Some blob -> MatchesETag blob.ETag
    | None -> MustNotExist

  let private deleteRow (dependencies: Dependencies) (slug: Slug) : CancellableTask<PublishSteps.Warning list> =
    cancellableTask {
      let! first = dependencies.DeletePostRow slug

      let! second =
        match first with
        | Ok() -> CancellableTask.singleton first
        | Error _ -> dependencies.DeletePostRow slug

      return
        match second with
        | Ok() -> []
        | Error error -> [ PublishSteps.PostRowNotDeleted error ]
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
        |> Result.mapError (UpcomingGate.FrontMatterUnreadable >> Refused >> ActionError<Error>.Logic)

      let series, parsed = dependencies.ParsePublished loaded.Posts loaded.Series

      let checkedDraft =
        TwoPoint.Site.Render.Check.draft
          dependencies.Stage
          dependencies.Images
          series
          (DateTimeOffset(dependencies.GetUtcNow()))
          parsed
          (Slug.value slug)
          draftText

      let errors =
        checkedDraft.Problems @ TwoPoint.Site.Render.Check.withoutPost dependencies.Stage parsed (Slug.value slug)
        |> List.filter TwoPoint.Site.Render.Problem.isError

      do!
        match errors with
        | [] -> Ok()
        | _ -> Result.Error(ActionError.Logic(HasErrors errors))

      let! written =
        dependencies.WriteWorkingBlob slug draftText (condition upcoming.Working)
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! draftETag =
        match written with
        | Written eTag -> Ok eTag
        | ConditionFailed -> Result.Error(ActionError.Logic(Refused UpcomingGate.WorkingBlobChanged))

      let! deleted =
        dependencies.DeletePublishedBlob slug upcoming.Published.ETag
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      do!
        match deleted with
        | Deleted -> Ok()
        | DeleteConditionFailed -> Result.Error(ActionError.Logic(Refused UpcomingGate.PublishedBlobChanged))

      let! rowWarnings = deleteRow dependencies slug
      let! rebuilt = PublishSteps.rebuild dependencies slug

      return
        Unpublished
          { PublishDate = None
            State = PostState.Draft
            WorkingETag = Some draftETag
            Production = rebuilt.Production
            Warnings = rowWarnings @ rebuilt.Warnings }
    }

[<RequireQualifiedAccess>]
module ChangeDate =

  /// `IfMatch` is the ETag of the working blob that the editor read. `None` means that the post has no pending edit.
  type Dto =
    { Slug: string
      IfMatch: string option
      PublishDate: string }

  type Error =
    | Refused of UpcomingGate.Refusal
    | HasErrors of TwoPoint.Site.Render.Problem list

  type Event = DateChanged of Moved

  type Dependencies =
    inherit PublishSteps.GatedDependencies
    abstract member WriteWorkingBlob: slug: Slug -> text: string -> condition: WriteCondition -> DependencyResult<WriteOutcome>

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  let private writeRow (dependencies: Dependencies) (post: PostRow) : CancellableTask<PublishSteps.Warning list> =
    cancellableTask {
      let! first = dependencies.UpsertPostRow post

      let! second =
        match first with
        | Ok() -> CancellableTask.singleton first
        | Error _ -> dependencies.UpsertPostRow post

      return
        match second with
        | Ok() -> []
        | Error error -> [ PublishSteps.CommentRowNotWritten error ]
    }

  let private follow
    (dependencies: Dependencies)
    (slug: Slug)
    (pending: StoredBlob)
    (publishDate: DateTime)
    : CancellableTask<string option * PublishSteps.Warning list> =
    cancellableTask {
      match TwoPoint.Site.Render.PostText.withPublishDate pending.Text (DateTimeOffset publishDate) with
      | Result.Error reason ->
        let error = { Dependency = Dependency.BlobStorage; Type = DependencyErrorType.Validation reason }
        return Some pending.ETag, [ PublishSteps.PendingDateNotUpdated error ]
      | Ok text ->
        match! dependencies.WriteWorkingBlob slug text (MatchesETag pending.ETag) with
        | Ok(Written eTag) -> return Some eTag, []
        | Ok ConditionFailed -> return None, [ PublishSteps.NewerTextKept ]
        | Error error -> return Some pending.ETag, [ PublishSteps.PendingDateNotUpdated error ]
    }

  let withDependencies (dependencies: Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! slug = dto.Slug |> Slug.create |> Result.mapError ActionError<Error>.Validation

      let! date =
        PublishTime.parseUtc "publishDate" dto.PublishDate
        |> Result.mapError ActionError<Error>.Validation

      let publishedAt = PublishTime.whole (dependencies.GetUtcNow())

      let! (upcoming: UpcomingGate.UpcomingPost), (loaded: PublishGate.Loaded) =
        UpcomingGate.load dependencies slug dto.IfMatch
        |> CancellableTask.map (Result.mapError (ActionError.mapLogic Refused))

      let! datedText =
        TwoPoint.Site.Render.PostText.withPublishDate upcoming.Published.Text (DateTimeOffset date)
        |> Result.mapError (UpcomingGate.FrontMatterUnreadable >> Refused >> ActionError<Error>.Logic)

      let series, parsed = dependencies.ParsePublished loaded.Posts loaded.Series

      let checkedPost =
        TwoPoint.Site.Render.Check.forPublish dependencies.Stage dependencies.Images series parsed (Slug.value slug) datedText

      let errors = checkedPost.Problems |> List.filter TwoPoint.Site.Render.Problem.isError

      do!
        match errors with
        | [] -> Ok()
        | _ -> Result.Error(ActionError.Logic(HasErrors errors))

      let! post = PublishSteps.row slug date datedText |> Result.mapError ActionError<Error>.Validation

      let! written =
        dependencies.WritePublishedBlob slug datedText (MatchesETag upcoming.Published.ETag)
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      do!
        match written with
        | Written _ -> Ok()
        | ConditionFailed -> Result.Error(ActionError.Logic(Refused UpcomingGate.PublishedBlobChanged))

      let! rowWarnings = writeRow dependencies post

      let! workingETag, pendingWarnings =
        match upcoming.Working with
        | Some pending -> follow dependencies slug pending date
        | None -> CancellableTask.singleton (None, [])
       

      let! rebuilt = PublishSteps.rebuild dependencies slug

      return
        DateChanged
          { PublishDate = Some date
            State = PublishSteps.Logic.state publishedAt date
            WorkingETag = workingETag
            Production = rebuilt.Production
            Warnings = rowWarnings @ pendingWarnings @ rebuilt.Warnings }
    }
