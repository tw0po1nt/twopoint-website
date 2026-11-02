namespace TwoPoint.Core.Posts.Actions

open TwoPoint.Core.Deploys
open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

[<RequireQualifiedAccess>]
module DeleteCheck =

  /// The pending edit of a linking post. `Post` is the parse of its text with the publish date, and `None` when the parse fails.
  /// `Problems` are the problems of its publish check.
  type Fix =
    { Slug: Slug
      Title: string
      ETag: string
      Post: TwoPoint.Site.Render.Post option
      Problems: TwoPoint.Site.Render.Problem list }

  /// A pending edit that the delete publishes first. `ETag` is the ETag of its working blob.
  type Edit =
    { Slug: Slug
      Title: string
      ETag: string }

  type Outcome =
    /// No published post links to the post.
    | PlainDelete
    /// The delete publishes these pending edits first. They remove each link to the post.
    | PublishAndDelete of edits: Edit list
    | Blocked of problems: TwoPoint.Site.Render.Problem list

  [<RequireQualifiedAccess>]
  module Logic =

    let private postError message =
      TwoPoint.Site.Render.Problem.error (TwoPoint.Site.Render.Place.Post 1) message

    let private editErrors (fix: Fix) =
      fix.Problems
      |> List.filter TwoPoint.Site.Render.Problem.isError
      |> List.map (fun problem -> postError $"The pending edit of the post '{fix.Slug}' cannot publish: {problem.Message}")

    /// `fixes` are the pending edits of the published posts. Only the pending edit of a linking post counts.
    let outcome
      (stage: TwoPoint.Site.Render.Stage)
      (published: TwoPoint.Site.Render.Post list)
      (slug: Slug)
      (fixes: Fix list)
      : Outcome =
      let linking = TwoPoint.Site.Render.Check.linkingPosts stage published (Slug.value slug)
      let fixOf linked = fixes |> List.tryFind (fun fix -> Slug.value fix.Slug = linked)
      let used = linking |> List.choose fixOf

      let unfixed =
        linking
        |> List.filter (fixOf >> Option.isNone)
        |> List.map (fun linked -> postError $"The post '{linked}' links to the post and has no pending edit.")

      let resultErrors =
        TwoPoint.Site.Render.Check.withoutPostAndEdits stage published (Slug.value slug) (used |> List.choose _.Post)
        |> List.filter TwoPoint.Site.Render.Problem.isError

      match linking, unfixed @ resultErrors @ List.collect editErrors used with
      | [], _ -> PlainDelete
      | _, [] ->
        PublishAndDelete
          [ for fix in used ->
              { Slug = fix.Slug
                Title = fix.Title
                ETag = fix.ETag } ]
      | _, problems -> Blocked problems

    /// The problem of a pending edit that the gate refuses. The outcome names the post.
    let refusal (refusal: PendingGate.Refusal) =
      let reason =
        match refusal with
        | PendingGate.NoPublishDate _ -> "The published text has no publish date."
        | PendingGate.FrontMatterUnreadable reason -> $"The front matter is not readable: {reason}"
        | PendingGate.PostNotFound _
        | PendingGate.NotPublished _
        | PendingGate.NoPendingEdit _
        | PendingGate.WorkingBlobChanged
        | PendingGate.PublishedBlobChanged -> "The pending edit changed during the check."

      postError reason

  /// The outcome, and the gate of each pending edit that a publish and delete publishes.
  type Checked =
    { Outcome: Outcome
      Gated: PendingGate.Gated list }

  let private fixOf
    (dependencies: PublishGate.Dependencies)
    (now: System.DateTime)
    (parsed: TwoPoint.Site.Render.Series list * TwoPoint.Site.Render.Post list)
    (slug: Slug)
    : DependencyResult<(Fix * PendingGate.Gated option) option> =
    cancellableTaskResult {
      let! published, working =
        CancellableTaskResult.parallelZip (dependencies.GetPublishedBlob slug) (dependencies.GetWorkingBlob slug)

      match published, working with
      | Some published, Some working ->
        match PendingGate.gate dependencies now parsed slug published working None with
        | Ok gated ->
          let series, _ = parsed

          return
            Some(
              { Slug = slug
                Title = (TwoPoint.Site.Render.PostText.split gated.Text).Title
                ETag = working.ETag
                Post = TwoPoint.Site.Render.Check.published dependencies.Stage series (Slug.value slug) gated.Text |> Result.toOption
                Problems = gated.Problems },
              Some gated
            )
        | Error refusal ->
          return
            Some(
              { Slug = slug
                Title = (TwoPoint.Site.Render.PostText.split working.Text).Title
                ETag = working.ETag
                Post = None
                Problems = [ Logic.refusal refusal ] },
              None
            )
      | _ -> return None
    }

  let rec private fixesOf dependencies now parsed (slugs: Slug list) (found: (Fix * PendingGate.Gated option) list) =
    cancellableTaskResult {
      match slugs with
      | [] -> return List.rev found
      | slug :: rest ->
        let! fix = fixOf dependencies now parsed slug
        return! fixesOf dependencies now parsed rest (Option.toList fix @ found)
    }

  /// `loaded` is the load of the post that the author deletes.
  let evaluate (dependencies: PublishGate.Dependencies) (slug: Slug) (loaded: PublishGate.Loaded) : DependencyResult<Checked> =
    cancellableTaskResult {
      let parsed = dependencies.ParsePublished loaded.Posts loaded.Series
      let _, posts = parsed
      let linking = TwoPoint.Site.Render.Check.linkingPosts dependencies.Stage posts (Slug.value slug) |> Set.ofList

      let linkingSlugs =
        loaded.Posts |> List.map _.Slug |> List.filter (fun other -> linking.Contains(Slug.value other))

      let! found = fixesOf dependencies (dependencies.GetUtcNow()) parsed linkingSlugs []
      let outcome = Logic.outcome dependencies.Stage posts slug (List.map fst found)

      let gated =
        match outcome with
        | PublishAndDelete edits ->
          edits
          |> List.choose (fun edit -> found |> List.tryPick (fun (fix, gated) -> if fix.Slug = edit.Slug then gated else None))
        | PlainDelete
        | Blocked _ -> []

      return { Outcome = outcome; Gated = gated }
    }

[<RequireQualifiedAccess>]
module CheckDelete =

  type Dto = { Slug: string }

  type Error =
    | PostNotFound of Slug
    | NotPublished of Slug

  type Event = Checked of DeleteCheck.Outcome

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  let withDependencies (dependencies: PublishGate.Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! slug = dto.Slug |> Slug.create |> Result.mapError ActionError<Error>.Validation

      let! (loaded: PublishGate.Loaded) =
        PublishGate.load dependencies slug |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      match loaded.Published, loaded.Working with
      | None, None -> return! Result.Error(ActionError.Logic(PostNotFound slug))
      | None, Some _ -> return! Result.Error(ActionError.Logic(NotPublished slug))
      | Some _, _ ->
        let! (checkedDelete: DeleteCheck.Checked) =
          DeleteCheck.evaluate dependencies slug loaded |> CancellableTaskResult.mapError ActionError<Error>.Dependency

        return Checked checkedDelete.Outcome
    }

[<RequireQualifiedAccess>]
module DeletePost =

  /// A pending edit that the delete publishes first. `IfMatch` is the ETag of its working blob that the delete check gave.
  type EditRef = { Slug: string; IfMatch: string }

  /// `IfMatch` is `None` when the post has no working blob. `ConfirmSlug` must equal the slug of a published post.
  /// `Edits` are the pending edits of the delete check. An empty list is a plain delete.
  type Dto =
    { Slug: string
      IfMatch: string option
      ConfirmSlug: string option
      Edits: EditRef list }

  type Error =
    | PostNotFound of Slug
    | WorkingBlobChanged
    | PublishedBlobChanged
    | SlugNotConfirmed of Slug
    | BreaksLinks of TwoPoint.Site.Render.Problem list
    /// The pending edits of the request are not the pending edits of the delete check.
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
    | PostKept of edits: Slug list * reason: KeptReason * production: ProductionDispatch * warnings: ChangeWarning list

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

    /// The pending edits to publish, when the request names the pending edits of the delete check.
    let confirm (requested: EditRef list) (outcome: DeleteCheck.Outcome) : Decision<DeleteCheck.Edit list, Error> =
      let asked = requested |> List.map (fun edit -> edit.Slug, edit.IfMatch)

      let matches (edits: DeleteCheck.Edit list) =
        List.sort asked = List.sort (edits |> List.map (fun edit -> Slug.value edit.Slug, edit.ETag))

      match outcome with
      | DeleteCheck.Blocked problems -> Decision.failure (BreaksLinks problems)
      | DeleteCheck.PlainDelete when List.isEmpty asked -> Decision.success []
      | DeleteCheck.PublishAndDelete edits when matches edits -> Decision.success edits
      | DeleteCheck.PlainDelete
      | DeleteCheck.PublishAndDelete _ -> Decision.failure EditsChanged

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

    /// A failed write of the first pending edit writes nothing, so the delete refuses.
    let firstEditError (failure: PublishSteps.WriteFailure) : ActionError<Error> =
      match failure with
      | PublishSteps.RevisionTaken
      | PublishSteps.PublishedConditionFailed -> ActionError.Logic EditsChanged
      | PublishSteps.WriteFailed error -> ActionError.Dependency error

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

  type private EditsPublished =
    { Published: Slug list
      Warnings: ChangeWarning list
      Failure: (Slug * PublishSteps.WriteFailure) option }

  /// Each edit has the publish steps of `PublishChanges`, with no dispatch. The first failed write stops the publish.
  let rec private publishEdits
    (dependencies: Dependencies)
    (edits: (PendingGate.Gated * PostRow) list)
    (published: Slug list)
    (warnings: ChangeWarning list)
    : CancellableTask<EditsPublished> =
    cancellableTask {
      match edits with
      | [] ->
        return
          { Published = List.rev published
            Warnings = warnings
            Failure = None }
      | (gated, post) :: rest ->
        let! written =
          PublishSteps.writeRevisionAndPublished
            dependencies
            gated.Slug
            gated.PublishedAt
            gated.Text
            gated.Published.Mark
            (MatchesETag gated.Published.ETag)

        match written with
        | Error failure ->
          return
            { Published = List.rev published
              Warnings = warnings
              Failure = Some(gated.Slug, failure) }
        | Ok() ->
          let! settled = PublishSteps.settle dependencies post gated.Working.ETag
          return! publishEdits dependencies rest (gated.Slug :: published) (warnings @ List.map Step settled)
    }

  let private keep
    (dependencies: Dependencies)
    (edits: EditsPublished)
    (reason: KeptReason)
    : CancellableTask<ActionResult<Outcome, Error>> =
    cancellableTask {
      let! rebuilt = PublishSteps.rebuildAll dependencies edits.Published
      return Ok(PostKept(edits.Published, reason, rebuilt.Production, edits.Warnings @ List.map Step rebuilt.Warnings))
    }

  let private deletePublished
    (dependencies: Dependencies)
    (slug: Slug)
    (published: StoredBlob)
    (working: StoredBlob option)
    (edits: (PendingGate.Gated * PostRow) list)
    : CancellableTask<ActionResult<Outcome, Error>> =
    cancellableTask {
      let! publishedEdits = publishEdits dependencies edits [] []

      match publishedEdits.Published, publishedEdits.Failure with
      | [], Some(_, failure) -> return Result.Error(Logic.firstEditError failure)
      | _, Some(editSlug, failure) -> return! keep dependencies publishedEdits (EditNotPublished(editSlug, failure))
      | _, None ->
        let! deleted = dependencies.DeletePublishedBlob slug published.ETag

        match publishedEdits.Published, deleted with
        | [], Error error -> return Result.Error(ActionError.Dependency error)
        | [], Ok DeleteConditionFailed -> return Result.Error(ActionError.Logic PublishedBlobChanged)
        | _, Error error -> return! keep dependencies publishedEdits (PublishedDeleteFailed error)
        | _, Ok DeleteConditionFailed -> return! keep dependencies publishedEdits PublishedBlobKept
        | _, Ok Deleted ->
          let! workingWarnings =
            match working with
            | Some blob -> dependencies.DeleteWorkingBlob slug blob.ETag |> CancellableTask.map Logic.workingWarnings
            | None -> CancellableTask.singleton []

          let! outcome = finishPublished dependencies slug publishedEdits.Published (publishedEdits.Warnings @ workingWarnings)
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

  let private rowsOf (checkedDelete: DeleteCheck.Checked) (edits: DeleteCheck.Edit list) =
    edits
    |> List.choose (fun edit -> checkedDelete.Gated |> List.tryFind (fun gated -> gated.Slug = edit.Slug))
    |> List.map (fun gated -> PublishSteps.row gated.Slug gated.PublishDate gated.Text |> Result.map (fun post -> gated, post))
    |> List.sequenceResultM

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
            let! (checkedDelete: DeleteCheck.Checked) =
              DeleteCheck.evaluate dependencies slug loaded |> CancellableTaskResult.mapError ActionError<Error>.Dependency

            let! edits = Logic.confirm dto.Edits checkedDelete.Outcome |> Result.mapError ActionError<Error>.Logic
            let! rows = rowsOf checkedDelete edits |> Result.mapError ActionError<Error>.Validation
            return! deletePublished dependencies slug published working rows
          }

      return PostDeleted outcome
    }
