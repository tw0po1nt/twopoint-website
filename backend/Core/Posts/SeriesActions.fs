namespace TwoPoint.Core.Posts.Actions

open TwoPoint.Core.Deploys
open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

open System

// ====================================================================================================================
// Save a series
// ====================================================================================================================

[<RequireQualifiedAccess>]
module SaveSeries =

  /// `Slug` is the slug of the stored record, or the slug of a new series. `IfMatch` is `None` for a new series.
  /// `NewSlug` is `None` when the slug stays.
  type Dto =
    { Slug: string
      IfMatch: string option
      NewSlug: string option
      Title: string
      Excerpt: string
      Image: string option }

  type FormProblem =
    | EmptyTitle
    | EmptyExcerpt
    | BadSlug of string
    | BadImage of string

  type Error =
    | Invalid of FormProblem list
    | SeriesNotFound of Slug
    | SeriesChanged
    | SlugTaken of Slug
    /// The site has a page for the series at its slug while a live post is in the series.
    | SlugLocked of live: SeriesPost list

  type Warning =
    | Rebuild of PublishSteps.Warning
    /// The record at the old slug changed after the rename read it, so it stays.
    | OldRecordKept
    | OldRecordNotDeleted of DependencyError

  type Saved =
    { Slug: Slug
      ETag: string
      Production: ProductionDispatch
      Warnings: Warning list }

  type Event = SeriesSaved of Saved

  type Plan =
    | Create of Slug
    | Update of Slug * eTag: string
    | Rename of from: Slug * eTag: string * target: Slug

  type Dependencies =
    inherit SeriesReads
    abstract member WriteSeriesBlob: slug: Slug -> text: string -> condition: WriteCondition -> DependencyResult<WriteOutcome>
    abstract member DeleteSeriesBlob: slug: Slug -> eTag: string -> DependencyResult<DeleteOutcome>
    abstract member DispatchSite: unit -> DependencyResult<SiteDispatch>

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  [<RequireQualifiedAccess>]
  module Logic =

    let private httpsImage (text: string) =
      match Uri.TryCreate(text, UriKind.Absolute) with
      | true, uri when uri.Scheme = Uri.UriSchemeHttps -> Some uri
      | _ -> None

    let form (slug: Slug) (dto: Dto) : Result<Slug * TwoPoint.Site.Render.Series, FormProblem list> =
      let title = dto.Title.Trim()
      let excerpt = dto.Excerpt.Trim()
      let image = dto.Image |> Option.map _.Trim() |> Option.filter (String.IsNullOrEmpty >> not)

      let target =
        match dto.NewSlug with
        | Some typed -> Slug.create typed |> Result.mapError (fun _ -> BadSlug typed)
        | None -> Ok slug

      let problems =
        [ if title = "" then EmptyTitle
          if excerpt = "" then EmptyExcerpt
          match target with
          | Error problem -> problem
          | Ok _ -> ()
          match image with
          | Some text when (httpsImage text).IsNone -> BadImage text
          | _ -> () ]

      match problems, target with
      | [], Ok target ->
        Ok(
          target,
          { Slug = Slug.value target
            Title = title
            Excerpt = excerpt
            Image = image |> Option.bind httpsImage }
        )
      | _ -> Error problems

    let decide
      (utcNow: DateTime)
      (store: SeriesStore)
      (slug: Slug)
      (ifMatch: string option)
      (target: Slug)
      : Decision<Plan, Error> =
      let record (slug: Slug) =
        store.Records |> List.tryFind (fun record -> record.Slug = Slug.value slug)

      match ifMatch, record slug with
      | None, _ when (record target).IsSome -> Decision.failure (SlugTaken target)
      | None, _ -> Decision.success (Create target)
      | Some _, None -> Decision.failure (SeriesNotFound slug)
      | Some eTag, Some stored when stored.ETag <> eTag -> Decision.failure SeriesChanged
      | Some eTag, Some _ when target = slug -> Decision.success (Update(slug, eTag))
      | Some _, Some _ when (record target).IsSome -> Decision.failure (SlugTaken target)
      | Some eTag, Some _ ->
        match SeriesPosts.live (SeriesPosts.inSeries utcNow store.Published store.Working (Slug.value slug)) with
        | [] -> Decision.success (Rename(slug, eTag, target))
        | live -> Decision.failure (SlugLocked live)

    let renameWarnings (deleted: Result<DeleteOutcome, DependencyError>) : Warning list =
      match deleted with
      | Ok Deleted -> []
      | Ok DeleteConditionFailed -> [ OldRecordKept ]
      | Error error -> [ OldRecordNotDeleted error ]

  let private write
    (dependencies: Dependencies)
    (target: Slug)
    (text: string)
    (condition: WriteCondition)
    (conflict: Error)
    : CancellableTask<ActionResult<string, Error>> =
    cancellableTaskResult {
      let! written =
        dependencies.WriteSeriesBlob target text condition
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      return!
        match written with
        | Written eTag -> Ok eTag
        | ConditionFailed -> Result.Error(ActionError.Logic conflict)
    }

  let withDependencies (dependencies: Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! slug = dto.Slug |> Slug.create |> Result.mapError ActionError<Error>.Validation
      let! target, series = Logic.form slug dto |> Result.mapError (Invalid >> ActionError.Logic)
      let! store = SeriesPosts.read dependencies |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! plan =
        Logic.decide (dependencies.GetUtcNow()) store slug dto.IfMatch target
        |> Result.mapError ActionError<Error>.Logic

      let text = TwoPoint.Site.Render.PostText.seriesRecord series

      let! eTag, renamed =
        match plan with
        | Create target ->
          write dependencies target text MustNotExist (SlugTaken target) |> CancellableTaskResult.map (fun eTag -> eTag, [])
        | Update(slug, eTag) ->
          write dependencies slug text (MatchesETag eTag) SeriesChanged |> CancellableTaskResult.map (fun eTag -> eTag, [])
        | Rename(from, oldETag, target) -> cancellableTaskResult {
            let! eTag = write dependencies target text MustNotExist (SlugTaken target)
            let! deleted = dependencies.DeleteSeriesBlob from oldETag
            return eTag, Logic.renameWarnings deleted
          }

      let! dispatched = dependencies.DispatchSite()

      return
        SeriesSaved
          { Slug = target
            ETag = eTag
            Production = SiteDispatch.production dispatched
            Warnings = renamed @ (PublishSteps.Logic.rebuildWarnings dispatched (Ok()) |> List.map Rebuild) }
    }

// ====================================================================================================================
// Delete a series
// ====================================================================================================================

[<RequireQualifiedAccess>]
module DeleteSeries =

  type Dto = { Slug: string; IfMatch: string }

  type Error =
    | SeriesNotFound of Slug
    | SeriesChanged
    | SeriesInUse of posts: SeriesPost list

  type Event = SeriesDeleted of Slug

  type Dependencies =
    inherit SeriesReads
    abstract member DeleteSeriesBlob: slug: Slug -> eTag: string -> DependencyResult<DeleteOutcome>

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  [<RequireQualifiedAccess>]
  module Logic =

    /// The site shows a series only while a live post is in it, and a deleted series has no post, so the site stays.
    let decide (utcNow: DateTime) (store: SeriesStore) (slug: Slug) (ifMatch: string) : Decision<unit, Error> =
      match store.Records |> List.tryFind (fun record -> record.Slug = Slug.value slug) with
      | None -> Decision.failure (SeriesNotFound slug)
      | Some record when record.ETag <> ifMatch -> Decision.failure SeriesChanged
      | Some _ ->
        match SeriesPosts.inSeries utcNow store.Published store.Working (Slug.value slug) with
        | [] -> Decision.success ()
        | posts -> Decision.failure (SeriesInUse posts)

  let withDependencies (dependencies: Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! slug = dto.Slug |> Slug.create |> Result.mapError ActionError<Error>.Validation
      let! store = SeriesPosts.read dependencies |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      do!
        Logic.decide (dependencies.GetUtcNow()) store slug dto.IfMatch
        |> Result.mapError ActionError<Error>.Logic

      let! deleted =
        dependencies.DeleteSeriesBlob slug dto.IfMatch
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      do!
        match deleted with
        | Deleted -> Ok()
        | DeleteConditionFailed -> Result.Error(ActionError.Logic SeriesChanged)

      return SeriesDeleted slug
    }
