namespace TwoPoint.Core.Posts.Actions

open TwoPoint.Core.Deploys
open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

open System
open System.Globalization

type PublishChoice =
  | ChooseNow
  /// An ISO 8601 UTC time, for example `2026-11-02T13:00:00Z`.
  | ChooseDate of text: string

type PublishTime =
  | Now
  | On of date: DateTime

[<RequireQualifiedAccess>]
module PublishTime =

  let whole (time: DateTime) =
    DateTime(time.Ticks - time.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc)

  let parse (choice: PublishChoice) : Result<PublishTime, string list> =
    match choice with
    | ChooseNow -> Ok Now
    | ChooseDate text ->
      match DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal) with
      | true, date when text.EndsWith 'Z' -> Ok(On(whole date.UtcDateTime))
      | _ -> Error [ "'publishDate' must be an ISO 8601 UTC time, for example 2026-11-02T13:00:00Z" ]

[<RequireQualifiedAccess>]
module PublishGate =

  type Refusal =
    | PostNotFound of Slug
    | AlreadyPublished of Slug
    | WorkingBlobChanged
    | DateInPast
    | FrontMatterUnreadable of reason: string

  type Dependencies =
    abstract member Stage: TwoPoint.Site.Render.Stage
    abstract member Images: TwoPoint.Site.Render.IImageSize
    abstract member GetUtcNow: unit -> DateTime
    abstract member GetPublishedBlob: slug: Slug -> DependencyResult<StoredBlob option>
    abstract member GetWorkingBlob: slug: Slug -> DependencyResult<StoredBlob option>
    abstract member GetPublishedPosts: unit -> DependencyResult<StoredPost list>
    abstract member GetSeriesRecords: unit -> DependencyResult<StoredSeries list>
    abstract member ParsePublished: PostCheck.ParsePublished

  type Gated =
    { PublishDate: DateTime
      PublishedAt: DateTime
      DatedText: string
      Working: StoredBlob
      Problems: TwoPoint.Site.Render.Problem list }

  [<RequireQualifiedAccess>]
  module Logic =

    let publishDate (now: DateTime) (time: PublishTime) : Decision<DateTime, Refusal> =
      match time with
      | Now -> Decision.success (PublishTime.whole now)
      | On date when date <= now -> Decision.failure DateInPast
      | On date -> Decision.success date

    let working
      (slug: Slug)
      (published: StoredBlob option)
      (working: StoredBlob option)
      (ifMatch: string option)
      : Decision<StoredBlob, Refusal> =
      match published, working with
      | Some _, _ -> Decision.failure (AlreadyPublished slug)
      | None, None -> Decision.failure (PostNotFound slug)
      | None, Some blob when ifMatch <> Some blob.ETag -> Decision.failure WorkingBlobChanged
      | None, Some blob -> Decision.success blob

  let evaluate
    (dependencies: Dependencies)
    (slug: Slug)
    (ifMatch: string option)
    (time: PublishTime)
    : CancellableTask<ActionResult<Gated, Refusal>> =
    cancellableTaskResult {
      let now = dependencies.GetUtcNow()
      let! publishDate = Logic.publishDate now time |> Result.mapError ActionError<Refusal>.Logic

      let! published, (working, (posts, records)) =
        CancellableTaskResult.parallelZip
          (dependencies.GetPublishedBlob slug)
          (CancellableTaskResult.parallelZip
            (dependencies.GetWorkingBlob slug)
            (CancellableTaskResult.parallelZip (dependencies.GetPublishedPosts()) (dependencies.GetSeriesRecords())))
        |> CancellableTaskResult.mapError ActionError<Refusal>.Dependency

      let! (blob: StoredBlob) = Logic.working slug published working ifMatch |> Result.mapError ActionError<Refusal>.Logic

      let! datedText =
        TwoPoint.Site.Render.PostText.withPublishDate blob.Text (DateTimeOffset publishDate)
        |> Result.mapError (FrontMatterUnreadable >> ActionError<Refusal>.Logic)

      let series, parsed = dependencies.ParsePublished posts records

      let checkedPost =
        TwoPoint.Site.Render.Check.forPublish dependencies.Stage dependencies.Images series parsed (Slug.value slug) datedText

      return
        ({ PublishDate = publishDate
           PublishedAt = PublishTime.whole now
           DatedText = datedText
           Working = blob
           Problems = checkedPost.Problems }
        : Gated)
    }

  let lift (refused: Refusal -> 'error) (error: ActionError<Refusal>) : ActionError<'error> =
    match error with
    | ActionError.Logic refusal -> ActionError.Logic(refused refusal)
    | ActionError.Dependency dependency -> ActionError.Dependency dependency
    | ActionError.Validation messages -> ActionError.Validation messages

// ====================================================================================================================
// Check a publish
// ====================================================================================================================

[<RequireQualifiedAccess>]
module CheckPublish =

  /// `IfMatch` is the ETag of the working blob that the editor read.
  type Dto =
    { Slug: string
      IfMatch: string option
      PublishTime: PublishChoice }

  type Error = Refused of PublishGate.Refusal

  type Event = Checked of TwoPoint.Site.Render.Problem list

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  let withDependencies (dependencies: PublishGate.Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! slug = dto.Slug |> Slug.create |> Result.mapError ActionError<Error>.Validation
      let! time = PublishTime.parse dto.PublishTime |> Result.mapError ActionError<Error>.Validation

      let! (gated: PublishGate.Gated) =
        PublishGate.evaluate dependencies slug dto.IfMatch time
        |> CancellableTask.map (Result.mapError (PublishGate.lift Refused))

      return Checked gated.Problems
    }

// ====================================================================================================================
// Publish a draft
// ====================================================================================================================

[<RequireQualifiedAccess>]
module PublishPost =

  /// `IfMatch` is the ETag of the working blob that the editor read.
  type Dto =
    { Slug: string
      IfMatch: string option
      PublishTime: PublishChoice }

  type Error =
    | Refused of PublishGate.Refusal
    | HasErrors of TwoPoint.Site.Render.Problem list
    | RevisionExists of Slug

  type Warning =
    | CommentRowNotWritten of DependencyError
    | PendingEditKept of DependencyError
    | NewerTextKept
    | ProductionRebuildFailed of DependencyError
    | StagingRebuildFailed of DependencyError
    | SiteNotRebuilt of DependencyError
    | DeployNotTracked of DependencyError

  type Outcome =
    { PublishDate: DateTime
      State: PostState
      Production: ProductionDispatch
      Warnings: Warning list }

  type Event = Published of Outcome

  type Dependencies =
    inherit PublishGate.Dependencies
    inherit PostChanges
    abstract member WriteRevisionBlob: slug: Slug -> time: DateTime -> text: string -> DependencyResult<WriteOutcome>
    abstract member DeleteRevisionBlob: slug: Slug -> time: DateTime -> DependencyResult<unit>
    abstract member WritePublishedBlob: slug: Slug -> text: string -> condition: WriteCondition -> DependencyResult<WriteOutcome>
    abstract member UpsertPostRow: post: PostRow -> DependencyResult<unit>
    abstract member DeleteWorkingBlob: slug: Slug -> eTag: string -> DependencyResult<DeleteOutcome>
    abstract member DispatchSite: unit -> DependencyResult<SiteDispatch>

  [<RequireQualifiedAccess>]
  module Logic =

    let state (publishedAt: DateTime) (publishDate: DateTime) =
      if publishDate > publishedAt then PostState.Upcoming else PostState.Published

    let warnings
      (row: Result<unit, DependencyError>)
      (deleted: Result<DeleteOutcome, DependencyError>)
      (dispatched: Result<SiteDispatch, DependencyError>)
      (tracked: Result<unit, DependencyError>)
      : Warning list =
      [ match row with
        | Error error -> CommentRowNotWritten error
        | Ok() -> ()

        match deleted with
        | Error error -> PendingEditKept error
        | Ok DeleteConditionFailed -> NewerTextKept
        | Ok Deleted -> ()

        match dispatched with
        | Error error -> SiteNotRebuilt error
        | Ok site ->
          match site.Production with
          | Error error -> ProductionRebuildFailed error
          | Ok _ -> ()

          match site.Staging with
          | Error error -> StagingRebuildFailed error
          | Ok _ -> ()

        match tracked with
        | Error error -> DeployNotTracked error
        | Ok() -> () ]

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  let withDependencies (dependencies: Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! slug = dto.Slug |> Slug.create |> Result.mapError ActionError<Error>.Validation
      let! time = PublishTime.parse dto.PublishTime |> Result.mapError ActionError<Error>.Validation

      let! (gated: PublishGate.Gated) =
        PublishGate.evaluate dependencies slug dto.IfMatch time
        |> CancellableTask.map (Result.mapError (PublishGate.lift Refused))

      let errors = gated.Problems |> List.filter TwoPoint.Site.Render.Problem.isError

      do!
        match errors with
        | [] -> Ok()
        | _ -> Result.Error(ActionError.Logic(HasErrors errors))

      let fields = TwoPoint.Site.Render.PostText.split gated.DatedText

      let! title =
        NonEmptyString.create (Some(nameof fields.Title)) fields.Title
        |> Result.mapError ActionError<Error>.Validation

      let! revision =
        dependencies.WriteRevisionBlob slug gated.PublishedAt gated.DatedText
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      match revision with
      | ConditionFailed -> return! Result.Error(ActionError.Logic(RevisionExists slug))
      | Written _ ->
        do!
          cancellableTask {
            let removeRevision () =
              dependencies.DeleteRevisionBlob slug gated.PublishedAt |> CancellableTask.map ignore

            match! dependencies.WritePublishedBlob slug gated.DatedText MustNotExist with
            | Ok(Written _) -> return Ok()
            | Ok ConditionFailed ->
              do! removeRevision ()
              return Result.Error(ActionError.Logic(Refused(PublishGate.AlreadyPublished slug)))
            | Error error ->
              do! removeRevision ()
              return Result.Error(ActionError<Error>.Dependency error)
          }

        return!
          cancellableTask {
            let upsert () =
              dependencies.UpsertPostRow
                { Title = title
                  Slug = slug
                  CreatedDate = gated.PublishDate }

            let! firstRow = upsert ()

            let! row =
              match firstRow with
              | Ok() -> CancellableTask.singleton firstRow
              | Error _ -> upsert ()

            let! deleted = dependencies.DeleteWorkingBlob slug gated.Working.ETag
            let changedDate = dependencies.GetUtcNow()
            let! dispatched = dependencies.DispatchSite()
            let! tracked = dependencies.RecordChange(PostChange.ofDispatch slug changedDate dispatched)

            return
              Ok(
                Published
                  { PublishDate = gated.PublishDate
                    State = Logic.state gated.PublishedAt gated.PublishDate
                    Production = SiteDispatch.production dispatched
                    Warnings = Logic.warnings row deleted dispatched tracked }
              )
          }
    }
