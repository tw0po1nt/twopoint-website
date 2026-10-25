namespace TwoPoint.Core.Posts.Actions

open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

open System
open System.Globalization

type PublishTime =
  | Now
  /// An ISO 8601 UTC time, for example `2026-11-02T13:00:00Z`.
  | On of date: string

[<RequireQualifiedAccess>]
module PublishGate =

  type Refusal =
    | PostNotFound of Slug
    | AlreadyPublished of Slug
    /// The working blob changed after the editor read it.
    | Stale
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

  /// `Text` is the working text with the publish date set.
  type Gated =
    { PublishDate: DateTime
      Text: string
      Working: StoredBlob
      Problems: TwoPoint.Site.Render.Problem list }

  [<RequireQualifiedAccess>]
  module Logic =

    let private whole (time: DateTime) = DateTime(time.Ticks - time.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc)

    let private parseUtc (text: string) =
      match DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal) with
      | true, date when text.EndsWith 'Z' -> Some date.UtcDateTime
      | _ -> None

    let publishDate (now: DateTime) (time: PublishTime) : ActionResult<DateTime, Refusal> =
      match time with
      | Now -> Ok(whole now)
      | On text ->
        match parseUtc text with
        | None -> Error(ActionError.Validation [ "'publishDate' must be an ISO 8601 UTC time, for example 2026-11-02T13:00:00Z" ])
        | Some date when date <= now -> Error(ActionError.Logic DateInPast)
        | Some date -> Ok date

    let working
      (slug: Slug)
      (published: StoredBlob option)
      (working: StoredBlob option)
      (ifMatch: string option)
      : Decision<StoredBlob, Refusal> =
      match published, working with
      | Some _, _ -> Decision.failure (AlreadyPublished slug)
      | None, None -> Decision.failure (PostNotFound slug)
      | None, Some blob when ifMatch <> Some blob.ETag -> Decision.failure Stale
      | None, Some blob -> Decision.success blob

  let evaluate
    (dependencies: Dependencies)
    (slug: Slug)
    (ifMatch: string option)
    (time: PublishTime)
    : CancellableTask<ActionResult<Gated, Refusal>> =
    cancellableTaskResult {
      let! published, (working, (posts, records)) =
        CancellableTaskResult.parallelZip
          (dependencies.GetPublishedBlob slug)
          (CancellableTaskResult.parallelZip
            (dependencies.GetWorkingBlob slug)
            (CancellableTaskResult.parallelZip (dependencies.GetPublishedPosts()) (dependencies.GetSeriesRecords())))
        |> CancellableTaskResult.mapError ActionError<Refusal>.Dependency

      let! publishDate = Logic.publishDate (dependencies.GetUtcNow()) time
      let! (blob: StoredBlob) = Logic.working slug published working ifMatch |> Result.mapError ActionError<Refusal>.Logic

      let! text =
        TwoPoint.Site.Render.PostText.withPublishDate blob.Text (DateTimeOffset publishDate)
        |> Result.mapError (FrontMatterUnreadable >> ActionError<Refusal>.Logic)

      let series, parsed = dependencies.ParsePublished posts records

      let checkedPost =
        TwoPoint.Site.Render.Check.forPublish dependencies.Stage dependencies.Images series parsed (Slug.value slug) text

      return
        ({ PublishDate = publishDate
           Text = text
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
      PublishTime: PublishTime }

  type Error = Refused of PublishGate.Refusal

  type Event = Checked of TwoPoint.Site.Render.Problem list

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  let withDependencies (dependencies: PublishGate.Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! slug = dto.Slug |> Slug.create |> Result.mapError ActionError<Error>.Validation

      let! (gated: PublishGate.Gated) =
        PublishGate.evaluate dependencies slug dto.IfMatch dto.PublishTime
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
      PublishTime: PublishTime }

  type Error =
    | Refused of PublishGate.Refusal
    | HasErrors of TwoPoint.Site.Render.Problem list

  /// A warning is a step after the write of the `published` blob that failed. The post stays published.
  type Warning =
    | CommentRowNotWritten of DependencyError
    | PendingEditKept of DependencyError
    | SiteNotRebuilt of DependencyError

  type Outcome =
    { PublishDate: DateTime
      State: PostState
      Warnings: Warning list }

  type Event = Published of Outcome

  type Dependencies =
    inherit PublishGate.Dependencies
    abstract member WriteRevisionBlob: slug: Slug -> time: string -> text: string -> DependencyResult<unit>
    abstract member WritePublishedBlob: slug: Slug -> text: string -> condition: WriteCondition -> DependencyResult<WriteOutcome>
    abstract member UpsertPostRow: post: NewPost -> DependencyResult<unit>
    abstract member DeleteWorkingBlob: slug: Slug -> eTag: string -> DependencyResult<DeleteOutcome>
    abstract member RebuildSite: unit -> DependencyResult<unit>

  [<RequireQualifiedAccess>]
  module Logic =

    let state (now: DateTime) (publishDate: DateTime) =
      if publishDate > now then PostState.Upcoming else PostState.Published

    let warnings
      (row: Result<unit, DependencyError>)
      (deleted: Result<DeleteOutcome, DependencyError>)
      (rebuilt: Result<unit, DependencyError>)
      : Warning list =
      [ match row with
        | Error error -> CommentRowNotWritten error
        | Ok() -> ()

        match deleted with
        | Error error -> PendingEditKept error
        | Ok Deleted
        | Ok DeleteConditionFailed -> ()

        match rebuilt with
        | Error error -> SiteNotRebuilt error
        | Ok() -> () ]

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  let withDependencies (dependencies: Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! slug = dto.Slug |> Slug.create |> Result.mapError ActionError<Error>.Validation

      let! (gated: PublishGate.Gated) =
        PublishGate.evaluate dependencies slug dto.IfMatch dto.PublishTime
        |> CancellableTask.map (Result.mapError (PublishGate.lift Refused))

      let errors = gated.Problems |> List.filter TwoPoint.Site.Render.Problem.isError

      do!
        match errors with
        | [] -> Ok()
        | _ -> Result.Error(ActionError.Logic(HasErrors errors))

      let! title =
        NonEmptyString.create (Some "title") (TwoPoint.Site.Render.PostText.split gated.Text).Title
        |> Result.mapError ActionError<Error>.Validation

      do!
        dependencies.WriteRevisionBlob slug (TwoPoint.Site.Render.Time.isoTime (DateTimeOffset gated.PublishDate)) gated.Text
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! written =
        dependencies.WritePublishedBlob slug gated.Text MustNotExist
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      match written with
      | ConditionFailed -> return! Result.Error(ActionError.Logic(Refused(PublishGate.AlreadyPublished slug)))
      | Written _ ->
        let finish =
          cancellableTask {
            let! row =
              dependencies.UpsertPostRow
                { Title = title
                  Slug = slug
                  CreatedDate = gated.PublishDate }

            let! deleted = dependencies.DeleteWorkingBlob slug gated.Working.ETag
            let! rebuilt = dependencies.RebuildSite()

            return
              Ok(
                Published
                  { PublishDate = gated.PublishDate
                    State = Logic.state (dependencies.GetUtcNow()) gated.PublishDate
                    Warnings = Logic.warnings row deleted rebuilt }
              )
          }

        return! finish
    }
