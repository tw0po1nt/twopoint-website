namespace TwoPoint.Core.Posts.Actions

open TwoPoint.Core.Deploys
open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

[<RequireQualifiedAccess>]
module RemovalCheck =

  /// The pending edit of a linking post. `Post` is the parse of its text with the publish date, and `None` when the parse fails.
  /// `Problems` are the problems of its publish check.
  type Fix =
    { Slug: Slug
      Title: string
      ETag: string
      Post: TwoPoint.Site.Render.Post option
      Problems: TwoPoint.Site.Render.Problem list }

  /// A pending edit that the removal publishes first. `ETag` is the ETag of its working blob.
  type Edit =
    { Slug: Slug
      Title: string
      ETag: string }

  type Outcome =
    /// No published post links to the post.
    | PlainRemoval
    /// The removal publishes these pending edits first. They remove each link to the post.
    | PublishAndRemove of edits: Edit list
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

      match unfixed @ resultErrors @ List.collect editErrors used with
      | [] when List.isEmpty linking -> PlainRemoval
      | [] ->
        PublishAndRemove
          [ for fix in used ->
              { Slug = fix.Slug
                Title = fix.Title
                ETag = fix.ETag } ]
      | problems -> Blocked problems

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

  /// The outcome, and the gate of each pending edit that a publish and remove publishes.
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

  /// `loaded` is the load of the post that leaves the site.
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
        | PublishAndRemove _ -> found |> List.choose snd
        | PlainRemoval
        | Blocked _ -> []

      return { Outcome = outcome; Gated = gated }
    }

[<RequireQualifiedAccess>]
module CheckRemoval =

  type Dto = { Slug: string }

  type Error =
    | PostNotFound of Slug
    | NotPublished of Slug

  type Event = Checked of RemovalCheck.Outcome

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
        let! (checkedRemoval: RemovalCheck.Checked) =
          RemovalCheck.evaluate dependencies slug loaded |> CancellableTaskResult.mapError ActionError<Error>.Dependency

        return Checked checkedRemoval.Outcome
    }

/// The steps that a delete and an unpublish share before the post leaves the site.
[<RequireQualifiedAccess>]
module RemovalSteps =

  /// A pending edit that the removal publishes first. `IfMatch` is the ETag of its working blob that the removal check gave.
  type EditRef = { Slug: string; IfMatch: string }

  type Refusal =
    | BreaksLinks of TwoPoint.Site.Render.Problem list
    /// The pending edits of the request are not the pending edits of the removal check.
    | EditsChanged

  /// A pending edit that the removal publishes, with the post-table row of its post.
  type GatedEdit = PendingGate.Gated * PostRow

  /// `Failure` is the first failed write. It stops the publish of the edits after it.
  type EditsPublished =
    { Published: Slug list
      Warnings: PublishSteps.Warning list
      Failure: (Slug * PublishSteps.WriteFailure) option }

  /// The post stays, and the dispatch ran for the published edits of `Edits`.
  type Kept =
    { Edits: Slug list
      Production: ProductionDispatch
      Warnings: PublishSteps.Warning list }

  [<RequireQualifiedAccess>]
  module Logic =

    let confirm (requested: EditRef list) (outcome: RemovalCheck.Outcome) : Decision<unit, Refusal> =
      let asked = requested |> List.map (fun edit -> edit.Slug, edit.IfMatch)

      let matches (edits: RemovalCheck.Edit list) =
        List.sort asked = List.sort (edits |> List.map (fun edit -> Slug.value edit.Slug, edit.ETag))

      match outcome with
      | RemovalCheck.Blocked problems -> Decision.failure (BreaksLinks problems)
      | RemovalCheck.PlainRemoval when List.isEmpty asked -> Decision.success ()
      | RemovalCheck.PublishAndRemove edits when matches edits -> Decision.success ()
      | RemovalCheck.PlainRemoval
      | RemovalCheck.PublishAndRemove _ -> Decision.failure EditsChanged

    /// A failed write of the first pending edit writes nothing, so the removal refuses.
    let firstEditError (failure: PublishSteps.WriteFailure) : ActionError<Refusal> =
      match failure with
      | PublishSteps.RevisionTaken
      | PublishSteps.PublishedConditionFailed -> ActionError.Logic EditsChanged
      | PublishSteps.WriteFailed error -> ActionError.Dependency error

  let private rowsOf (checkedRemoval: RemovalCheck.Checked) : Result<GatedEdit list, string list> =
    checkedRemoval.Gated
    |> List.map (fun gated -> PublishSteps.row gated.Slug gated.PublishDate gated.Text |> Result.map (fun post -> gated, post))
    |> List.sequenceResultM

  /// Runs the removal check again and compares the request with its outcome. `loaded` is the load of the post that leaves the site.
  let prepare
    (dependencies: PublishGate.Dependencies)
    (slug: Slug)
    (loaded: PublishGate.Loaded)
    (requested: EditRef list)
    : CancellableTask<ActionResult<GatedEdit list, Refusal>> =
    cancellableTaskResult {
      let! (checkedRemoval: RemovalCheck.Checked) =
        RemovalCheck.evaluate dependencies slug loaded |> CancellableTaskResult.mapError ActionError<Refusal>.Dependency

      do! Logic.confirm requested checkedRemoval.Outcome |> Result.mapError ActionError<Refusal>.Logic
      return! rowsOf checkedRemoval |> Result.mapError ActionError<Refusal>.Validation
    }

  let rec private publishFrom
    (dependencies: PublishSteps.GatedDependencies)
    (edits: GatedEdit list)
    (published: Slug list)
    (warnings: PublishSteps.Warning list)
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
          return! publishFrom dependencies rest (gated.Slug :: published) (warnings @ settled)
    }

  /// Each edit has the publish steps of `PublishChanges`, with no dispatch.
  let publishEdits (dependencies: PublishSteps.GatedDependencies) (edits: GatedEdit list) : CancellableTask<EditsPublished> =
    publishFrom dependencies edits [] []

  /// A step after the edits failed. With no published edit, the removal refuses with `refusal`. Otherwise the post stays,
  /// and the dispatch runs for the published edits.
  let fail
    (dependencies: PublishSteps.GatedDependencies)
    (edits: EditsPublished)
    (refusal: ActionError<'error>)
    (kept: Kept -> 'outcome)
    : CancellableTask<Result<'outcome, ActionError<'error>>> =
    cancellableTask {
      match edits.Published with
      | [] -> return Result.Error refusal
      | published ->
        let! rebuilt = PublishSteps.rebuildAll dependencies published

        return
          Ok(
            kept
              { Edits = published
                Production = rebuilt.Production
                Warnings = edits.Warnings @ rebuilt.Warnings }
          )
    }
