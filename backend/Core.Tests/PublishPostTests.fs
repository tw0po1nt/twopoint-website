module TwoPoint.Core.Tests.PublishPostTests

open TwoPoint.Core.Deploys
open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Shared
open TwoPoint.Core.Tests.Fixtures
open TwoPoint.Core.Util
open TwoPoint.Site.Render

open Expecto
open FsToolkit.ErrorHandling
open IcedTasks

open System
open System.Globalization
open System.Threading

let private utcNow = DateTime(2026, 3, 1, 9, 30, 45, DateTimeKind.Utc)

let private slug value = Slug.Unsafe.create value

let private isoOf (time : DateTime) = time.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)

let private down name : DependencyError = { Dependency = Dependency.GitHub; Type = Unknown(exn $"{name} is down") }

let private okImages =
  { new IImageSize with
      member _.Size _ = Result.Ok { Width = 1600; Height = 900 } }

let private draftText =
  "---\nauthor: Matthew Watt\ntitle: My draft\nexcerpt: An excerpt\ncategory: One-offs\n---\n\nA body.\n"

let private taggedText =
  "---\nauthor: Matthew Watt\ntitle: My draft\nexcerpt: An excerpt\ncategory: One-offs\ntags:\n  - FSharp\n---\n\nA body.\n"

let private livePost =
  "---\npublishDate: 2026-01-01T00:00:00Z\nauthor: A\ntitle: Live\nexcerpt: E\ncategory: One-offs\n---\n\nLive body.\n"

let private pendingEdit =
  "---\npublishDate: 2026-01-01T00:00:00Z\nauthor: A\ntitle: Live\nexcerpt: E\ncategory: One-offs\n---\n\nEdited.\n"

type private DispatchBehavior =
  | BothSucceed
  | StagingFails
  | ProductionFails
  | WholeCallFails

type private FakeStore(published : Map<string, string>, working : Map<string, string>) =
  let log = ResizeArray<string>()
  let mutable published = published
  let mutable working = working
  let mutable version = 1
  let mutable publishedVersions = Map.empty<string, int>
  let publishedVersion (name : string) = publishedVersions |> Map.tryFind name |> Option.defaultValue 1
  let bumpPublished (name : string) = publishedVersions <- publishedVersions |> Map.add name (publishedVersion name + 1)
  let mutable marks = Map.empty<string, MigratedMark>
  let mutable revisions = Map.empty<string, string>
  let mutable rows = Map.empty<string, PostRow>
  let mutable images : IImageSize = okImages
  let mutable rowFailures = 0
  let mutable dispatch = BothSucceed
  let mutable revisionWriteFails = false
  let mutable publishedWriteFails = false
  let mutable publishedWriteFailsFor = Set.empty<string>
  let mutable beforeDelete = ignore
  let mutable beforePublishedWrite = ignore
  let mutable now = utcNow
  let mutable changes : PostChange list = []
  let mutable recordFails = false
  let mutable comments = Map.empty<string, string list>
  let mutable deliveries = Map.empty<string, string list>
  let mutable commentDeleteFails = false
  let mutable workingWriteFails = false
  let mutable publishedDeleteFails = false

  let writeWorking (slug : Slug) (text : string) (condition : WriteCondition) : DependencyResult<WriteOutcome> =
    let name = Slug.value slug

    let allowed =
      match condition, working |> Map.tryFind name with
      | MustNotExist, None -> true
      | MatchesETag eTag, Some _ -> eTag = $"w{version}"
      | _ -> false

    if workingWriteFails then
      CancellableTaskResult.error (down "The working container")
    elif allowed then
      log.Add "working"
      version <- version + 1
      working <- working |> Map.add name text
      CancellableTaskResult.singleton (Written $"w{version}")
    else
      CancellableTaskResult.singleton ConditionFailed

  member _.Log = List.ofSeq log
  member _.Published = published
  member _.Mark (name : string) = marks |> Map.tryFind name |> Option.defaultValue NotMigrated
  member _.MarkMigrated (name : string) = marks <- marks |> Map.add name Migrated
  member _.Working = working
  member _.Revisions = revisions
  member _.Rows = rows
  member _.Comments = comments
  member _.Deliveries = deliveries
  member _.CommentDeleteFails with set value = commentDeleteFails <- value
  member _.WorkingWriteFails with set value = workingWriteFails <- value
  member _.PublishedDeleteFails with set value = publishedDeleteFails <- value
  member _.AddComments (name : string) (ids : string list) = comments <- comments |> Map.add name ids
  member _.AddDeliveries (name : string) (ids : string list) = deliveries <- deliveries |> Map.add name ids
  member _.Changes = changes
  member _.RecordFails with set value = recordFails <- value

  member _.Images with set value = images <- value
  member _.RowFailures with set value = rowFailures <- value
  member _.Dispatch with set value = dispatch <- value
  member _.RevisionWriteFails with set value = revisionWriteFails <- value
  member _.PublishedWriteFails with set value = publishedWriteFails <- value
  member _.PublishedWriteFailsFor with set value = publishedWriteFailsFor <- value
  member _.BeforeDelete with set action = beforeDelete <- action
  member _.BeforePublishedWrite with set action = beforePublishedWrite <- action

  member _.Overwrite (name : string) (text : string) =
    version <- version + 1
    working <- working |> Map.add name text

  member _.Publish (name : string) (text : string) =
    bumpPublished name
    published <- published |> Map.add name text

  member _.AddRevision (name : string) (time : string) = revisions <- revisions |> Map.add $"{name}/{time}" "older"

  interface PostChanges with
    member _.RecordChange change =
      if recordFails then
        CancellableTaskResult.error (down "The change table")
      else
        changes <- change :: changes
        CancellableTaskResult.singleton ()

  interface PublishSteps.GatedDependencies with
    member _.Stage = Stage.Staging
    member _.Images = images
    member _.GetUtcNow () = now
    member _.ParsePublished = PostCheck.parsePublished Stage.Staging

    member this.GetPublishedBlob slug =
      published
      |> Map.tryFind (Slug.value slug)
      |> Option.map (fun text -> { Text = text; ETag = $"p{publishedVersion (Slug.value slug)}"; Mark = this.Mark(Slug.value slug) })
      |> CancellableTaskResult.singleton

    member _.GetWorkingBlob slug =
      working
      |> Map.tryFind (Slug.value slug)
      |> Option.map (fun text -> { Text = text; ETag = $"w{version}"; Mark = NotMigrated })
      |> CancellableTaskResult.singleton

    member _.GetPublishedPosts () =
      published
      |> Map.toList
      |> List.map (fun (name, text) -> { StoredPost.Slug = slug name; Text = text; ETag = $"p{publishedVersion name}" })
      |> CancellableTaskResult.singleton

    member _.GetSeriesRecords () = CancellableTaskResult.singleton []

    member _.WriteRevisionBlob slug time text =
      let key = $"{Slug.value slug}/{isoOf time}"

      if revisionWriteFails then
        CancellableTaskResult.error (down "The revisions container")
      elif revisions.ContainsKey key then
        CancellableTaskResult.singleton ConditionFailed
      else
        log.Add $"revision {key}"
        revisions <- revisions |> Map.add key text
        CancellableTaskResult.singleton (Written "r1")

    member _.DeleteRevisionBlob slug time =
      log.Add "delete revision"
      revisions <- revisions |> Map.remove $"{Slug.value slug}/{isoOf time}"
      CancellableTaskResult.singleton ()

    member _.WritePublishedBlob slug text mark condition =
      beforePublishedWrite ()

      if publishedWriteFails || publishedWriteFailsFor.Contains(Slug.value slug) then
        CancellableTaskResult.error (down "The published container")
      else
        match condition, published |> Map.tryFind (Slug.value slug) with
        | MustNotExist, None ->
          log.Add "published"
          published <- published |> Map.add (Slug.value slug) text
          marks <- marks |> Map.add (Slug.value slug) mark
          CancellableTaskResult.singleton (Written $"p{publishedVersion (Slug.value slug)}")
        | MatchesETag eTag, Some _ when eTag = $"p{publishedVersion (Slug.value slug)}" ->
          log.Add "published"
          bumpPublished (Slug.value slug)
          published <- published |> Map.add (Slug.value slug) text
          marks <- marks |> Map.add (Slug.value slug) mark
          CancellableTaskResult.singleton (Written $"p{publishedVersion (Slug.value slug)}")
        | _ -> CancellableTaskResult.singleton ConditionFailed

    member _.UpsertPostRow post =
      log.Add "row"

      if rowFailures > 0 then
        rowFailures <- rowFailures - 1
        CancellableTaskResult.error (down "The post table")
      else
        rows <- rows |> Map.add (Slug.value post.Slug) post
        CancellableTaskResult.singleton ()

    member _.DeleteWorkingBlob slug eTag =
      beforeDelete ()
      log.Add "delete working"

      match working |> Map.tryFind (Slug.value slug) with
      | Some _ when eTag = $"w{version}" ->
        working <- working |> Map.remove (Slug.value slug)
        CancellableTaskResult.singleton Deleted
      | _ -> CancellableTaskResult.singleton DeleteConditionFailed

    member _.DispatchSite () =
      log.Add "dispatch"
      now <- now.AddSeconds 5.

      let dispatched : Result<Dispatch, DependencyError> = Ok(Queued(RunNumber 7L))

      match dispatch with
      | WholeCallFails -> CancellableTaskResult.error (down "GitHub")
      | BothSucceed -> CancellableTaskResult.singleton { Production = dispatched; Staging = dispatched }
      | StagingFails -> CancellableTaskResult.singleton { Production = dispatched; Staging = Error(down "The staging repo") }
      | ProductionFails -> CancellableTaskResult.singleton { Production = Error(down "The production repo"); Staging = dispatched }

  member _.AddRow (row : PostRow) = rows <- rows |> Map.add (Slug.value row.Slug) row

  interface UpcomingGate.Dependencies with
    member _.WriteWorkingBlob slug text condition = writeWorking slug text condition

  interface Unpublish.Dependencies with
    member _.DeletePublishedBlob slug eTag =
      beforePublishedWrite ()
      log.Add "delete published"

      match published |> Map.tryFind (Slug.value slug) with
      | _ when publishedDeleteFails -> CancellableTaskResult.error (down "The published container")
      | Some _ when eTag = $"p{publishedVersion (Slug.value slug)}" ->
        published <- published |> Map.remove (Slug.value slug)
        CancellableTaskResult.singleton Deleted
      | _ -> CancellableTaskResult.singleton DeleteConditionFailed

    member _.DeletePostRow slug =
      log.Add "delete row"

      if rowFailures > 0 then
        rowFailures <- rowFailures - 1
        CancellableTaskResult.error (down "The post table")
      else
        rows <- rows |> Map.remove (Slug.value slug)
        CancellableTaskResult.singleton ()

  interface DeletePost.Dependencies with
    member this.DeletePublishedBlob slug eTag = (this :> Unpublish.Dependencies).DeletePublishedBlob slug eTag
    member this.DeletePostRow slug = (this :> Unpublish.Dependencies).DeletePostRow slug

    member _.DeleteRevisionBlobs slug =
      log.Add "delete revisions"
      revisions <- revisions |> Map.filter (fun key _ -> not (key.StartsWith $"{Slug.value slug}/"))
      CancellableTaskResult.singleton ()

    member _.DeletePostComments slug =
      log.Add "delete comments"

      if commentDeleteFails then
        CancellableTaskResult.error (down "The comment table")
      else
        comments <- comments |> Map.remove (Slug.value slug)
        CancellableTaskResult.singleton ()

    member _.HasPostRecords slug =
      CancellableTaskResult.singleton (rows.ContainsKey(Slug.value slug) || comments.ContainsKey(Slug.value slug))


let private store published working = FakeStore(Map.ofList published, Map.ofList working)

let private run (task : CancellableTask<'a>) = (task CancellationToken.None).Result

let private publish (fake : FakeStore) name ifMatch time =
  run (PublishPost.withDependencies fake { Slug = name; IfMatch = ifMatch; PublishTime = time })

let private check (fake : FakeStore) name ifMatch time =
  run (CheckPublish.withDependencies fake { Slug = name; IfMatch = ifMatch; PublishTime = time })

let private outcomeOf result =
  match result with
  | Ok(PublishPost.Event.Published outcome) -> outcome
  | Error error -> failtestf "Expected a publish, got %A" error

let private commenterId = "7d1f0c55-3c1b-4a43-9a0e-2b5f6f0d8e21"

let private commentOnPostsOf (fake : FakeStore) (clock : DateTime) =
  let commenter =
    Commenter.create commenterId "commenter@example.com" (Some "Commenter") "2026-01-01T00:00:00.0000000Z" "new"
    |> ok

  let dependencies =
    { new PostComment.Dependencies with
        member _.GetPostBySlug slug =
          fake.Rows
          |> Map.tryFind (Slug.value slug)
          |> Option.map (fun row ->
            Post.create
              (Slug.value row.Slug)
              (NonEmptyString.value row.Title)
              (row.CreatedDate.ToString("o", CultureInfo.InvariantCulture))
              PostCommentStats.Zero
            |> ok)
          |> CancellableTaskResult.singleton
        member _.GetUtcNow () = clock
        member _.GetPostedCommentById _ = CancellableTaskResult.singleton None
        member _.Stage = TwoPoint.Site.Render.Stage.Production
        member _.GetCommenterByValidationId _ = CancellableTaskResult.singleton (Some commenter)
        member _.NewCommentId () = CommentId.ofGuid (Guid "0b6c1f0e-2f0a-4c8e-9a51-6d7c2b1e3f40")
        member _.CreateComment _ _ commenter newComment _ approval _ =
          let posted =
            Comment.create
              "0b6c1f0e-2f0a-4c8e-9a51-6d7c2b1e3f40"
              "2026-03-02T00:00:00.0000000Z"
              {| Approval = approval.ToString(); CreatedDate = "2026-03-02T00:00:00.0000000Z" |}
              None
              (CommentWriter.Commenter commenter)
              None
              "A comment"
            |> ok

          CancellableTaskResult.singleton { posted with Content = newComment.Content }
        member _.NewReplyEmailStopToken () = ReplyEmailStopToken.ofGuid Guid.Empty
        member _.CreateReplyEmailStop _ = CancellableTaskResult.singleton ()
        member _.QueueNewCommentNotice _ = CancellableTaskResult.singleton ()
        member _.QueueReplyEmail _ _ = CancellableTaskResult.singleton () }

  let dto =
    { NewComment.Dto.Post = NewComment.Post "my-draft"
      NewComment.Dto.ValidationId = NewComment.ValidationId commenterId
      NewComment.Dto.Comment = NewComment.Comment "A comment"
      NewComment.Dto.ReplyTo = None
      NewComment.Dto.ReplyEmails = NewComment.ReplyEmails false }

  (PostComment.withDependencies dependencies dto CancellationToken.None).Result

let private publishTime = DateTime(2026, 3, 1, 9, 30, 45, DateTimeKind.Utc)

let private warningsOf fake =
  (publish fake "my-draft" (Some "w1") ChooseNow |> outcomeOf).Warnings

[<Tests>]
let tests =
  testList "Publish a draft" [
    test "a publish writes the revision, the published blob, the row, and deletes the working blob, and then dispatches" {
      let fake = store [] [ "my-draft", draftText ]
      let outcome = publish fake "my-draft" (Some "w1") ChooseNow |> outcomeOf

      Expect.equal
        fake.Log
        [ "revision my-draft/2026-03-01T09:30:45Z"; "published"; "row"; "delete working"; "dispatch" ]
        "The steps of the spec, in order"

      Expect.equal
        outcome
        { PublishDate = publishTime
          State = TwoPoint.Core.Posts.PostState.Published
          Production = Dispatched(Queued(RunNumber 7L))
          Warnings = [] }
        "The outcome has the production run"

      Expect.isEmpty fake.Working "The working blob is gone"
    }

    test "the published blob and the revision contain the text with the publish date, and the other fields stay" {
      let fake = store [] [ "my-draft", taggedText ]
      publish fake "my-draft" (Some "w1") ChooseNow |> ignore

      let text = fake.Published["my-draft"]
      Expect.equal (Post.summary text).PublishDate (Some(DateTimeOffset publishTime)) "The publish date is the publish time"
      Expect.equal (Post.summary text).Title (Some "My draft") "The title"
      Expect.stringContains text "FSharp" "The tags stay"
      Expect.equal fake.Revisions["my-draft/2026-03-01T09:30:45Z"] text "The revision is a copy"
    }

    test "the row has the title and the publish date" {
      let fake = store [] [ "my-draft", draftText ]
      publish fake "my-draft" (Some "w1") ChooseNow |> ignore

      let row = fake.Rows["my-draft"]
      Expect.equal (NonEmptyString.value row.Title) "My draft" "The title"
      Expect.equal row.CreatedDate publishTime "The created date"
    }

    test "a publish with a future date is upcoming, and the text and the row have that date in whole seconds" {
      let fake = store [] [ "my-draft", draftText ]
      let outcome = publish fake "my-draft" (Some "w1") (ChooseDate "2026-04-01T13:00:00.900Z") |> outcomeOf
      let date = DateTime(2026, 4, 1, 13, 0, 0, DateTimeKind.Utc)

      Expect.equal outcome.State TwoPoint.Core.Posts.PostState.Upcoming "An upcoming post"
      Expect.equal outcome.PublishDate date "Whole seconds"
      Expect.equal fake.Rows["my-draft"].CreatedDate date "The row has the date"
      Expect.equal (Post.summary fake.Published["my-draft"]).PublishDate (Some(DateTimeOffset date)) "The text has the date"
    }

    test "the revision is named by the time of the publish, so a second publish at the same date has its own revision" {
      let fake = store [] [ "my-draft", draftText ]
      publish fake "my-draft" (Some "w1") (ChooseDate "2026-04-01T13:00:00Z") |> ignore
      Expect.equal (fake.Revisions |> Map.keys |> List.ofSeq) [ "my-draft/2026-03-01T09:30:45Z" ] "The time of the publish"
    }

    test "a publish with a past date is refused, and the store does not change" {
      let fake = store [] [ "my-draft", draftText ]

      match publish fake "my-draft" (Some "w1") (ChooseDate "2026-02-01T13:00:00Z") with
      | Error(ActionError.Logic(PublishPost.Refused PublishGate.DateInPast)) -> ()
      | other -> failtestf "Expected DateInPast, got %A" other

      Expect.isEmpty fake.Log "No write and no dispatch"
    }

    test "a publish date that is not a UTC time is a validation error before any store read" {
      let fake = store [] [ "my-draft", draftText ]

      match publish fake "my-draft" (Some "w1") (ChooseDate "next week") with
      | Error(ActionError.Validation _) -> ()
      | other -> failtestf "Expected a validation error, got %A" other

      Expect.isEmpty fake.Log "No write and no dispatch"
    }

    test "a post with an error is refused, and the store does not change" {
      let broken = draftText.Replace("A body.", "See [it](/posts/nowhere/).")
      let fake = store [] [ "my-draft", broken ]

      match publish fake "my-draft" (Some "w1") ChooseNow with
      | Error(ActionError.Logic(PublishPost.HasErrors problems)) ->
        Expect.isNonEmpty problems "The errors"
        Expect.isTrue (problems |> List.forall Problem.isError) "Only errors"
      | other -> failtestf "Expected HasErrors, got %A" other

      Expect.isEmpty fake.Log "No write and no dispatch"
      Expect.equal fake.Working (Map.ofList [ "my-draft", broken ]) "The working blob stays"
      Expect.isEmpty fake.Published "No published blob"
    }

    test "a card image that the build cannot fetch is refused with the error of the build" {
      let image = "https://example.com/card.jpg"
      let withImage = draftText.Replace("category: One-offs", $"category: One-offs\nimage: {image}")
      let fake = store [] [ "my-draft", withImage ]

      fake.Images <-
        { new IImageSize with
            member _.Size _ = Result.Error "HTTP 404" }

      match publish fake "my-draft" (Some "w1") ChooseNow with
      | Error(ActionError.Logic(PublishPost.HasErrors problems)) ->
        Expect.equal
          (problems |> List.map _.Message)
          [ $"Couldn't crop the Open Graph image {image}: HTTP 404." ]
          "The message of the build"
      | other -> failtestf "Expected HasErrors, got %A" other

      Expect.isEmpty fake.Log "No write and no dispatch"
    }

    test "a warning does not block the publish" {
      let fake = store [] [ "my-draft", taggedText ]

      match check fake "my-draft" (Some "w1") ChooseNow with
      | Ok(CheckPublish.Event.Checked problems) ->
        Expect.isNonEmpty problems "The post has the warning for the year tag"
        Expect.isFalse (problems |> List.exists Problem.isError) "No error"
      | Error error -> failtestf "Expected a check, got %A" error

      Expect.equal (warningsOf fake) [] "Published"
    }

    test "the check gives the problems and writes nothing" {
      let broken = draftText.Replace("A body.", "See [it](/posts/nowhere/).")
      let fake = store [] [ "my-draft", broken ]

      match check fake "my-draft" (Some "w1") ChooseNow with
      | Ok(CheckPublish.Event.Checked problems) -> Expect.isTrue (problems |> List.exists Problem.isError) "The error"
      | Error error -> failtestf "Expected a check, got %A" error

      Expect.isEmpty fake.Log "No write"
    }

    test "the check of a past date is refused" {
      let fake = store [] [ "my-draft", draftText ]

      match check fake "my-draft" (Some "w1") (ChooseDate "2026-02-01T13:00:00Z") with
      | Error(ActionError.Logic(CheckPublish.Refused PublishGate.DateInPast)) -> ()
      | other -> failtestf "Expected DateInPast, got %A" other
    }

    test "a failed dispatch leaves the post published and gives a warning" {
      let fake = store [] [ "my-draft", draftText ]
      fake.Dispatch <- WholeCallFails

      match publish fake "my-draft" (Some "w1") ChooseNow |> outcomeOf with
      | { Warnings = [ PublishSteps.SiteNotRebuilt _ ]; Production = ProductionDispatch.Failed } -> ()
      | other -> failtestf "Expected SiteNotRebuilt, got %A" other

      Expect.isTrue (fake.Published.ContainsKey "my-draft") "The published blob stays"
      Expect.isTrue (fake.Rows.ContainsKey "my-draft") "The row stays"
      Expect.isEmpty fake.Working "The working blob is gone"
    }

    test "a publish records the change with the production run number" {
      let fake = store [] [ "my-draft", draftText ]
      publish fake "my-draft" (Some "w1") ChooseNow |> ignore

      Expect.equal
        fake.Changes
        [ { Slug = slug "my-draft"
            Change = { ChangedDate = utcNow; Production = Dispatched(Queued(RunNumber 7L)) } } ]
        "The change has the run number of the dispatch"
    }

    test "the change is dated before the dispatch, so the run that the dispatch queued deploys it" {
      let fake = store [] [ "my-draft", draftText ]
      publish fake "my-draft" (Some "w1") ChooseNow |> ignore

      match fake.Changes with
      | [ { Change = change } ] ->
        let queuedByDispatch = utcNow.AddSeconds 2.
        Expect.isLessThan change.ChangedDate queuedByDispatch "A run queued by the dispatch is later than the change"
      | other -> failtestf "Expected one change, got %A" other
    }

    test "a failed dispatch records a change with no run number" {
      let fake = store [] [ "my-draft", draftText ]
      fake.Dispatch <- WholeCallFails
      publish fake "my-draft" (Some "w1") ChooseNow |> ignore

      Expect.equal
        (fake.Changes |> List.map _.Change.Production)
        [ ProductionDispatch.Failed ]
        "The change has no run number"
    }

    test "a record of the change that fails gives a warning, and the post stays published" {
      let fake = store [] [ "my-draft", draftText ]
      fake.RecordFails <- true

      match warningsOf fake with
      | [ PublishSteps.DeployNotTracked _ ] -> ()
      | other -> failtestf "Expected DeployNotTracked, got %A" other

      Expect.isTrue (fake.Published.ContainsKey "my-draft") "The published blob stays"
    }

    test "a dispatch that fails for one repo names that repo" {
      let staging = store [] [ "my-draft", draftText ]
      staging.Dispatch <- StagingFails

      match publish staging "my-draft" (Some "w1") ChooseNow |> outcomeOf with
      | { Warnings = [ PublishSteps.StagingRebuildFailed _ ]; Production = Dispatched(Queued _) } -> ()
      | other -> failtestf "Expected StagingRebuildFailed, got %A" other

      let production = store [] [ "my-draft", draftText ]
      production.Dispatch <- ProductionFails

      match publish production "my-draft" (Some "w1") ChooseNow |> outcomeOf with
      | { Warnings = [ PublishSteps.ProductionRebuildFailed _ ]; Production = ProductionDispatch.Failed } -> ()
      | other -> failtestf "Expected ProductionRebuildFailed, got %A" other
    }

    test "a failed row write is tried again, and the second try gives no warning" {
      let fake = store [] [ "my-draft", draftText ]
      fake.RowFailures <- 1

      Expect.equal (warningsOf fake) [] "No warning"
      Expect.equal (fake.Log |> List.filter ((=) "row")) [ "row"; "row" ] "Two tries"
      Expect.isTrue (fake.Rows.ContainsKey "my-draft") "The row"
    }

    test "two failed row writes give a warning, and the later steps still run" {
      let fake = store [] [ "my-draft", draftText ]
      fake.RowFailures <- 2

      match warningsOf fake with
      | [ PublishSteps.CommentRowNotWritten _ ] -> ()
      | other -> failtestf "Expected CommentRowNotWritten, got %A" other

      Expect.equal (List.rev fake.Log |> List.take 2) [ "dispatch"; "delete working" ] "The delete and the dispatch ran"
      Expect.isTrue (fake.Published.ContainsKey "my-draft") "The published blob stays"
    }

    test "a working blob that changes during the publish stays as the pending edit, with a warning" {
      let fake = store [] [ "my-draft", draftText ]
      fake.BeforeDelete <- fun () -> fake.Overwrite "my-draft" "newer text"

      Expect.equal (warningsOf fake) [ PublishSteps.NewerTextKept ] "The warning"
      Expect.equal fake.Working["my-draft"] "newer text" "The newer text stays"
    }

    test "a publish with an old ETag is refused, and the store does not change" {
      let fake = store [] [ "my-draft", draftText ]

      match publish fake "my-draft" (Some "w0") ChooseNow with
      | Error(ActionError.Logic(PublishPost.Refused PublishGate.WorkingBlobChanged)) -> ()
      | other -> failtestf "Expected WorkingBlobChanged, got %A" other

      Expect.isEmpty fake.Log "No write"
    }

    test "a publish of a post that is published is refused, and the published blob stays" {
      let fake = store [ "live", livePost ] [ "live", pendingEdit ]

      match publish fake "live" (Some "w1") ChooseNow with
      | Error(ActionError.Logic(PublishPost.Refused(PublishGate.AlreadyPublished _))) -> ()
      | other -> failtestf "Expected AlreadyPublished, got %A" other

      Expect.isEmpty fake.Log "No write"
      Expect.equal fake.Published["live"] livePost "The published text stays"
    }

    test "a publish that loses the race for the published blob is refused, and leaves no revision" {
      let fake = store [] [ "my-draft", draftText ]
      fake.BeforePublishedWrite <- fun () -> fake.Publish "my-draft" livePost

      match publish fake "my-draft" (Some "w1") ChooseNow with
      | Error(ActionError.Logic(PublishPost.Refused(PublishGate.AlreadyPublished _))) -> ()
      | other -> failtestf "Expected AlreadyPublished, got %A" other

      Expect.isEmpty fake.Revisions "The revision is deleted"
      Expect.equal fake.Published["my-draft"] livePost "The other published text stays"
      Expect.equal fake.Working["my-draft"] draftText "The working blob stays"
      Expect.isEmpty fake.Rows "No row"
    }

    test "a failed revision write leaves the draft as it was" {
      let fake = store [] [ "my-draft", draftText ]
      fake.RevisionWriteFails <- true

      match publish fake "my-draft" (Some "w1") ChooseNow with
      | Error(ActionError.Dependency _) -> ()
      | other -> failtestf "Expected a dependency error, got %A" other

      Expect.isEmpty fake.Log "No other write and no dispatch"
      Expect.equal fake.Working (Map.ofList [ "my-draft", draftText ]) "The working blob stays"
      Expect.isEmpty fake.Published "No published blob"
    }

    test "a failed write of the published blob leaves the draft as it was, and deletes the revision" {
      let fake = store [] [ "my-draft", draftText ]
      fake.PublishedWriteFails <- true

      match publish fake "my-draft" (Some "w1") ChooseNow with
      | Error(ActionError.Dependency _) -> ()
      | other -> failtestf "Expected a dependency error, got %A" other

      Expect.isEmpty fake.Revisions "No revision"
      Expect.isEmpty fake.Rows "No row"
      Expect.equal fake.Working (Map.ofList [ "my-draft", draftText ]) "The working blob stays"
      Expect.isEmpty fake.Published "No published blob"
    }

    test "a revision that exists is not replaced" {
      let fake = store [] [ "my-draft", draftText ]
      fake.AddRevision "my-draft" "2026-03-01T09:30:45Z"

      match publish fake "my-draft" (Some "w1") ChooseNow with
      | Error(ActionError.Logic(PublishPost.RevisionExists _)) -> ()
      | other -> failtestf "Expected RevisionExists, got %A" other

      Expect.equal fake.Revisions["my-draft/2026-03-01T09:30:45Z"] "older" "The older revision stays"
      Expect.isEmpty fake.Published "No published blob"
    }

    test "a publish of a post with no working blob is refused" {
      let fake = store [] []

      match publish fake "nothing" (Some "w1") ChooseNow with
      | Error(ActionError.Logic(PublishPost.Refused(PublishGate.PostNotFound _))) -> ()
      | other -> failtestf "Expected PostNotFound, got %A" other
    }

    test "a comment on a new post succeeds after its publish date with no manual row" {
      let fake = store [] [ "my-draft", draftText ]
      publish fake "my-draft" (Some "w1") ChooseNow |> outcomeOf |> ignore

      match commentOnPostsOf fake (publishTime.AddMinutes 1.) with
      | Ok(PostComment.Event.Posted _) -> ()
      | other -> failtestf "Expected Posted, got %A" other
    }

    test "a comment on an upcoming post is refused before its publish date and posted after it" {
      let fake = store [] [ "my-draft", draftText ]
      publish fake "my-draft" (Some "w1") (ChooseDate "2026-04-01T13:00:00Z") |> outcomeOf |> ignore

      match commentOnPostsOf fake (DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc)) with
      | Error(ActionError.Logic(PostComment.Error.PostUpcoming _)) -> ()
      | other -> failtestf "Expected PostUpcoming, got %A" other

      match commentOnPostsOf fake (DateTime(2026, 4, 1, 13, 0, 1, DateTimeKind.Utc)) with
      | Ok(PostComment.Event.Posted _) -> ()
      | other -> failtestf "Expected Posted, got %A" other
    }
  ]

let private changedText =
  pendingEdit.Replace("title: Live", "title: Live, edited").Replace("Edited.", "Edited, again.")

let private publishChanges (fake : FakeStore) name ifMatch updateDate =
  run (PublishChanges.withDependencies fake { Slug = name; IfMatch = ifMatch; UpdateDate = updateDate })

let private checkChanges (fake : FakeStore) name ifMatch updateDate =
  run (CheckChanges.withDependencies fake { Slug = name; IfMatch = ifMatch; UpdateDate = updateDate })

let private discard (fake : FakeStore) name ifMatch =
  let dependencies = fake :> PublishSteps.GatedDependencies

  let discardChanges =
    DiscardChanges.withDependencies
      { new DiscardChanges.Dependencies with
          member _.GetPublishedBlob slug = dependencies.GetPublishedBlob slug
          member _.GetWorkingBlob slug = dependencies.GetWorkingBlob slug
          member _.DeleteWorkingBlob slug eTag = dependencies.DeleteWorkingBlob slug eTag }

  run (discardChanges { Slug = name; IfMatch = ifMatch })

let private changesOutcomeOf result =
  match result with
  | Ok(PublishChanges.Event.Published outcome) -> outcome
  | Error error -> failtestf "Expected published changes, got %A" error

let private refusedWith expected result =
  match result with
  | Error(ActionError.Logic(PublishChanges.Refused refusal)) -> Expect.equal refusal expected "The refusal"
  | other -> failtestf "Expected %A, got %A" expected other

[<Tests>]
let changesTests =
  testList "Publish changes" [
    test "the publish writes the revision, the published blob, and the row with the new title, deletes the working blob, and then dispatches" {
      let fake = store [ "live", livePost ] [ "live", changedText ]
      let outcome = publishChanges fake "live" (Some "w1") None |> changesOutcomeOf

      Expect.equal
        fake.Log
        [ "revision live/2026-03-01T09:30:45Z"; "published"; "row"; "delete working"; "dispatch" ]
        "The steps, in order"

      Expect.equal fake.Published["live"] changedText "The published text is the pending edit"
      Expect.equal fake.Revisions["live/2026-03-01T09:30:45Z"] changedText "The revision is a copy"
      Expect.equal (NonEmptyString.value fake.Rows["live"].Title) "Live, edited" "The new title"
      Expect.equal fake.Rows["live"].CreatedDate (DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)) "The publish date stays"
      Expect.isEmpty fake.Working "The working blob is gone"

      Expect.equal
        outcome
        { PublishDate = DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
          State = TwoPoint.Core.Posts.PostState.Published
          Production = Dispatched(Queued(RunNumber 7L))
          Warnings = [] }
        "The outcome has the production run"
    }

    test "the publish keeps the migrated mark of the published blob" {
      let fake = store [ "live", livePost ] [ "live", changedText ]
      fake.MarkMigrated "live"

      publishChanges fake "live" (Some "w1") None |> changesOutcomeOf |> ignore
      Expect.equal fake.Published["live"] changedText "The changes are published"
      Expect.equal (fake.Mark "live") Migrated "The mark stays"
    }

    test "the publish gives no mark to a post without one" {
      let fake = store [ "live", livePost ] [ "live", changedText ]
      publishChanges fake "live" (Some "w1") None |> changesOutcomeOf |> ignore
      Expect.equal (fake.Mark "live") NotMigrated "No mark"
    }

    test "the publish records the change with the production run number" {
      let fake = store [ "live", livePost ] [ "live", changedText ]
      publishChanges fake "live" (Some "w1") None |> changesOutcomeOf |> ignore

      Expect.equal
        fake.Changes
        [ { Slug = slug "live"
            Change = { ChangedDate = utcNow; Production = Dispatched(Queued(RunNumber 7L)) } } ]
        "The change has the run number of the dispatch"
    }

    test "an update date is written into the published text and the revision" {
      let fake = store [ "live", livePost ] [ "live", changedText ]
      publishChanges fake "live" (Some "w1") (Some "2026-02-04T08:00:00.700Z") |> changesOutcomeOf |> ignore

      Expect.stringContains fake.Published["live"] "updateDate: 2026-02-04T08:00:00Z" "The update date in whole seconds"
      Expect.equal fake.Revisions["live/2026-03-01T09:30:45Z"] fake.Published["live"] "The revision has the date"
      Expect.stringContains fake.Published["live"] "Edited, again." "The body stays"
    }

    test "no update date leaves the text as the author saved it" {
      let fake = store [ "live", livePost ] [ "live", changedText ]
      publishChanges fake "live" (Some "w1") None |> changesOutcomeOf |> ignore
      Expect.equal fake.Published["live"] changedText "The text"
    }

    test "an update date that is not a UTC time is a validation error before any store read" {
      let fake = store [ "live", livePost ] [ "live", changedText ]

      match publishChanges fake "live" (Some "w1") (Some "next week") with
      | Error(ActionError.Validation _) -> ()
      | other -> failtestf "Expected a validation error, got %A" other

      Expect.isEmpty fake.Log "No write and no dispatch"
    }

    test "a post with an error is refused, and the store does not change" {
      let broken = changedText.Replace("Edited, again.", "See [it](/posts/nowhere/).")
      let fake = store [ "live", livePost ] [ "live", broken ]

      match publishChanges fake "live" (Some "w1") None with
      | Error(ActionError.Logic(PublishChanges.HasErrors problems)) ->
        Expect.isTrue (problems |> List.forall Problem.isError) "Only errors"
        Expect.isNonEmpty problems "The errors"
      | other -> failtestf "Expected HasErrors, got %A" other

      Expect.isEmpty fake.Log "No write and no dispatch"
      Expect.equal fake.Published["live"] livePost "The published text stays"
      Expect.equal fake.Working["live"] broken "The pending edit stays"
    }

    test "the check gives the problems and writes nothing" {
      let broken = changedText.Replace("Edited, again.", "See [it](/posts/nowhere/).")
      let fake = store [ "live", livePost ] [ "live", broken ]

      match checkChanges fake "live" (Some "w1") None with
      | Ok(CheckChanges.Event.Checked problems) -> Expect.isTrue (problems |> List.exists Problem.isError) "The error"
      | Error error -> failtestf "Expected a check, got %A" error

      Expect.isEmpty fake.Log "No write"
    }

    test "the check refuses a draft and a published post with no pending edit" {
      let draft = store [] [ "my-draft", draftText ]

      match checkChanges draft "my-draft" (Some "w1") None with
      | Error(ActionError.Logic(CheckChanges.Refused(PendingGate.NotPublished _))) -> ()
      | other -> failtestf "Expected NotPublished, got %A" other

      let clean = store [ "live", livePost ] []

      match checkChanges clean "live" (Some "w1") None with
      | Error(ActionError.Logic(CheckChanges.Refused(PendingGate.NoPendingEdit _))) -> ()
      | other -> failtestf "Expected NoPendingEdit, got %A" other
    }

    test "a draft is refused, and the draft stays" {
      let fake = store [] [ "my-draft", draftText ]
      publishChanges fake "my-draft" (Some "w1") None |> refusedWith (PendingGate.NotPublished(slug "my-draft"))
      Expect.isEmpty fake.Log "No write"
      Expect.equal fake.Working["my-draft"] draftText "The draft stays"
    }

    test "a published post with no pending edit is refused" {
      let fake = store [ "live", livePost ] []
      publishChanges fake "live" (Some "w1") None |> refusedWith (PendingGate.NoPendingEdit(slug "live"))
      Expect.isEmpty fake.Log "No write"
    }

    test "a post that does not exist is refused" {
      let fake = store [] []
      publishChanges fake "nothing" (Some "w1") None |> refusedWith (PendingGate.PostNotFound(slug "nothing"))
    }

    test "an old ETag is refused, and the store does not change" {
      let fake = store [ "live", livePost ] [ "live", changedText ]
      publishChanges fake "live" (Some "w0") None |> refusedWith PendingGate.WorkingBlobChanged
      Expect.isEmpty fake.Log "No write"
    }

    test "a published text that changes during the publish is refused, and leaves no revision" {
      let fake = store [ "live", livePost ] [ "live", changedText ]
      fake.BeforePublishedWrite <- fun () -> fake.Publish "live" (livePost.Replace("Live body.", "Other body."))

      publishChanges fake "live" (Some "w1") None |> refusedWith PendingGate.PublishedBlobChanged

      Expect.isEmpty fake.Revisions "The revision is deleted"
      Expect.stringContains fake.Published["live"] "Other body." "The other published text stays"
      Expect.equal fake.Working["live"] changedText "The pending edit stays"
      Expect.isEmpty fake.Rows "No row"
    }

    test "a revision that exists is not replaced" {
      let fake = store [ "live", livePost ] [ "live", changedText ]
      fake.AddRevision "live" "2026-03-01T09:30:45Z"

      match publishChanges fake "live" (Some "w1") None with
      | Error(ActionError.Logic(PublishChanges.RevisionExists _)) -> ()
      | other -> failtestf "Expected RevisionExists, got %A" other

      Expect.equal fake.Published["live"] livePost "The published text stays"
    }

    test "a failed write of the published blob leaves the pending edit as it was, and deletes the revision" {
      let fake = store [ "live", livePost ] [ "live", changedText ]
      fake.PublishedWriteFails <- true

      match publishChanges fake "live" (Some "w1") None with
      | Error(ActionError.Dependency _) -> ()
      | other -> failtestf "Expected a dependency error, got %A" other

      Expect.isEmpty fake.Revisions "No revision"
      Expect.isEmpty fake.Rows "No row"
      Expect.equal fake.Working["live"] changedText "The pending edit stays"
    }

    test "two failed row writes give a warning, and the later steps still run" {
      let fake = store [ "live", livePost ] [ "live", changedText ]
      fake.RowFailures <- 2

      match (publishChanges fake "live" (Some "w1") None |> changesOutcomeOf).Warnings with
      | [ PublishSteps.CommentRowNotWritten _ ] -> ()
      | other -> failtestf "Expected CommentRowNotWritten, got %A" other

      Expect.isEmpty fake.Rows "No row yet"
      Expect.equal (List.rev fake.Log |> List.take 2) [ "dispatch"; "delete working" ] "The later steps ran"

    }

    test "a working blob that changes during the publish stays as the pending edit, with a warning" {
      let fake = store [ "live", livePost ] [ "live", changedText ]
      fake.BeforeDelete <- fun () -> fake.Overwrite "live" "newer text"

      Expect.equal (publishChanges fake "live" (Some "w1") None |> changesOutcomeOf).Warnings [ PublishSteps.NewerTextKept ] "The warning"
      Expect.equal fake.Working["live"] "newer text" "The newer text stays"
    }

    test "a failed dispatch leaves the changes published and gives a warning" {
      let fake = store [ "live", livePost ] [ "live", changedText ]
      fake.Dispatch <- WholeCallFails

      match publishChanges fake "live" (Some "w1") None |> changesOutcomeOf with
      | { Warnings = [ PublishSteps.SiteNotRebuilt _ ]; Production = ProductionDispatch.Failed } -> ()
      | other -> failtestf "Expected SiteNotRebuilt, got %A" other

      Expect.equal fake.Published["live"] changedText "The published text stays"
    }

    test "a card image that the build cannot fetch is refused with the error of the build" {
      let image = "https://example.com/card.jpg"
      let withImage = changedText.Replace("category: One-offs", $"category: One-offs\nimage: {image}")
      let fake = store [ "live", livePost ] [ "live", withImage ]

      fake.Images <-
        { new IImageSize with
            member _.Size _ = Result.Error "HTTP 404" }

      match publishChanges fake "live" (Some "w1") None with
      | Error(ActionError.Logic(PublishChanges.HasErrors problems)) ->
        Expect.equal
          (problems |> List.map _.Message)
          [ $"Couldn't crop the Open Graph image {image}: HTTP 404." ]
          "The message of the build"
      | other -> failtestf "Expected HasErrors, got %A" other

      Expect.isEmpty fake.Log "No write and no dispatch"
    }

    test "the publish date of the pending edit does not move the publish date of the post" {
      let moved = changedText.Replace("publishDate: 2026-01-01T00:00:00Z", "publishDate: 2027-05-05T00:00:00Z")
      let fake = store [ "live", livePost ] [ "live", moved ]
      let outcome = publishChanges fake "live" (Some "w1") None |> changesOutcomeOf
      let date = DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)

      Expect.equal outcome.PublishDate date "The outcome"
      Expect.equal fake.Rows["live"].CreatedDate date "The row"
      Expect.equal (Post.summary fake.Published["live"]).PublishDate (Some(DateTimeOffset date)) "The published blob"
      Expect.equal fake.Revisions["live/2026-03-01T09:30:45Z"] fake.Published["live"] "The revision"
    }

    test "a published text with no publish date is refused" {
      let undated = livePost.Replace("publishDate: 2026-01-01T00:00:00Z\n", "")
      let fake = store [ "live", undated ] [ "live", changedText ]
      publishChanges fake "live" (Some "w1") None |> refusedWith (PendingGate.NoPublishDate(slug "live"))
      Expect.isEmpty fake.Log "No write"
    }
  ]

[<Tests>]
let discardTests =
  testList "Discard changes" [
    test "the discard deletes only the working blob" {
      let fake = store [ "live", livePost ] [ "live", changedText ]

      match discard fake "live" (Some "w1") with
      | Ok DiscardChanges.Discarded -> ()
      | other -> failtestf "Expected Discarded, got %A" other

      Expect.equal fake.Log [ "delete working" ] "Only the delete"
      Expect.isEmpty fake.Working "The working blob is gone"
      Expect.equal fake.Published["live"] livePost "The published text stays"
      Expect.isEmpty fake.Revisions "No revision"
      Expect.isEmpty fake.Rows "No row"
    }

    test "a draft is refused, so the draft is not deleted" {
      let fake = store [] [ "my-draft", draftText ]

      match discard fake "my-draft" (Some "w1") with
      | Error(ActionError.Logic(DiscardChanges.Refused(PendingGate.NotPublished _))) -> ()
      | other -> failtestf "Expected NotPublished, got %A" other

      Expect.isEmpty fake.Log "No delete"
      Expect.equal fake.Working["my-draft"] draftText "The draft stays"
    }

    test "a published post with no pending edit is refused" {
      let fake = store [ "live", livePost ] []

      match discard fake "live" (Some "w1") with
      | Error(ActionError.Logic(DiscardChanges.Refused(PendingGate.NoPendingEdit _))) -> ()
      | other -> failtestf "Expected NoPendingEdit, got %A" other
    }

    test "an old ETag is refused, and the pending edit stays" {
      let fake = store [ "live", livePost ] [ "live", changedText ]

      match discard fake "live" (Some "w0") with
      | Error(ActionError.Logic(DiscardChanges.Refused PendingGate.WorkingBlobChanged)) -> ()
      | other -> failtestf "Expected WorkingBlobChanged, got %A" other

      Expect.isEmpty fake.Log "No delete"
      Expect.equal fake.Working["live"] changedText "The pending edit stays"
    }

    test "a working blob that changes before the delete is refused, and the newer text stays" {
      let fake = store [ "live", livePost ] [ "live", changedText ]
      fake.BeforeDelete <- fun () -> fake.Overwrite "live" "newer text"

      match discard fake "live" (Some "w1") with
      | Error(ActionError.Logic(DiscardChanges.Refused PendingGate.WorkingBlobChanged)) -> ()
      | other -> failtestf "Expected WorkingBlobChanged, got %A" other

      Expect.equal fake.Working["live"] "newer text" "The newer text stays"
    }
  ]

let private soonDate = DateTime(2026, 4, 1, 13, 0, 0, DateTimeKind.Utc)

let private upcomingPost =
  livePost.Replace("2026-01-01T00:00:00Z", "2026-04-01T13:00:00Z")

let private upcomingEdit =
  pendingEdit.Replace("2026-01-01T00:00:00Z", "2026-04-01T13:00:00Z").Replace("Edited.", "Edited for the move.")

let private upcomingRow : PostRow =
  { Title = NonEmptyString.create (Some "Title") "Live" |> ok
    Slug = slug "soon"
    CreatedDate = soonDate }

let private upcomingStore published working =
  let fake = store published working
  fake.AddRow upcomingRow
  fake

let private unpublish (fake : FakeStore) name ifMatch =
  run (Unpublish.withDependencies fake { Slug = name; IfMatch = ifMatch; Edits = [] })

let private changeDate (fake : FakeStore) name ifMatch date =
  run (ChangeDate.withDependencies fake { Slug = name; IfMatch = ifMatch; PublishDate = date })

let private unpublishedOf result =
  match result with
  | Ok(Unpublish.Event.Unpublished moved) -> moved
  | other -> failtestf "Expected an unpublish, got %A" other

let private dateChangedOf result =
  match result with
  | Ok(ChangeDate.Event.DateChanged moved) -> moved
  | Error error -> failtestf "Expected a date change, got %A" error

let private unpublishRefusedWith expected result =
  match result with
  | Error(ActionError.Logic(Unpublish.Refused refusal)) -> Expect.equal refusal expected "The refusal"
  | other -> failtestf "Expected %A, got %A" expected other

let private dateRefusedWith expected result =
  match result with
  | Error(ActionError.Logic(ChangeDate.Refused refusal)) -> Expect.equal refusal expected "The refusal"
  | other -> failtestf "Expected %A, got %A" expected other

[<Tests>]
let unpublishTests =
  testList "Unpublish an upcoming post" [
    test "an unpublish with a pending edit keeps the pending edit as the draft text, and deletes the row" {
      let fake = upcomingStore [ "soon", upcomingPost ] [ "soon", upcomingEdit ]
      let moved = unpublish fake "soon" (Some "w1") |> unpublishedOf

      Expect.equal fake.Log [ "working"; "delete published"; "delete row"; "dispatch" ] "The steps, in order"
      Expect.isEmpty fake.Published "The published blob is gone"
      Expect.isEmpty fake.Rows "The row is gone"
      Expect.stringContains fake.Working["soon"] "Edited for the move." "The pending edit is the draft text"
      Expect.equal (Post.summary fake.Working["soon"]).PublishDate None "The publish date is removed"

      Expect.equal
        moved
        { PublishDate = None
          State = TwoPoint.Core.Posts.PostState.Draft
          WorkingETag = Some "w2"
          Production = Dispatched(Queued(RunNumber 7L))
          Warnings = [] }
        "The outcome has the ETag of the draft and the production run"
    }

    test "an unpublish with no pending edit makes the published content the draft text" {
      let fake = upcomingStore [ "soon", upcomingPost ] []
      unpublish fake "soon" None |> unpublishedOf |> ignore

      Expect.stringContains fake.Working["soon"] "Live body." "The published content"
      Expect.equal (Post.summary fake.Working["soon"]).PublishDate None "The publish date is removed"
      Expect.isEmpty fake.Published "The published blob is gone"
    }

    test "an unpublish keeps the revisions" {
      let fake = upcomingStore [ "soon", upcomingPost ] []
      fake.AddRevision "soon" "2026-02-01T00:00:00Z"
      unpublish fake "soon" None |> unpublishedOf |> ignore
      Expect.equal (fake.Revisions |> Map.keys |> List.ofSeq) [ "soon/2026-02-01T00:00:00Z" ] "The revision stays"
    }

    test "an unpublish records the change with the production run number" {
      let fake = upcomingStore [ "soon", upcomingPost ] []
      unpublish fake "soon" None |> unpublishedOf |> ignore

      Expect.equal
        fake.Changes
        [ { Slug = slug "soon"
            Change = { ChangedDate = utcNow; Production = Dispatched(Queued(RunNumber 7L)) } } ]
        "The change has the run number of the dispatch"
    }

    test "an unpublish of a post with a past date is refused, and the store does not change" {
      let fake = store [ "live", livePost ] []
      unpublish fake "live" None |> unpublishRefusedWith (UpcomingGate.NotUpcoming(slug "live"))
      Expect.isEmpty fake.Log "No write and no dispatch"
      Expect.equal fake.Published["live"] livePost "The published text stays"
    }

    test "an unpublish of a draft is refused" {
      let fake = store [] [ "my-draft", draftText ]
      unpublish fake "my-draft" (Some "w1") |> unpublishRefusedWith ((UpcomingGate.Gate(PendingGate.NotPublished(slug "my-draft"))))
      Expect.isEmpty fake.Log "No write"
    }

    test "an unpublish of a post that does not exist is refused" {
      let fake = store [] []
      unpublish fake "nothing" None |> unpublishRefusedWith ((UpcomingGate.Gate(PendingGate.PostNotFound(slug "nothing"))))
    }

    test "an old ETag is refused, and a missing ETag is refused when a pending edit exists" {
      let fake = upcomingStore [ "soon", upcomingPost ] [ "soon", upcomingEdit ]
      unpublish fake "soon" (Some "w0") |> unpublishRefusedWith (UpcomingGate.Gate PendingGate.WorkingBlobChanged)
      unpublish fake "soon" None |> unpublishRefusedWith (UpcomingGate.Gate PendingGate.WorkingBlobChanged)
      Expect.isEmpty fake.Log "No write"
    }

    test "an ETag for a post with no pending edit is refused" {
      let fake = upcomingStore [ "soon", upcomingPost ] []
      unpublish fake "soon" (Some "w1") |> unpublishRefusedWith (UpcomingGate.Gate PendingGate.WorkingBlobChanged)
      Expect.isEmpty fake.Log "No write"
    }

    test "a pending edit with an error in its draft text does not stop the unpublish" {
      let broken = upcomingEdit.Replace("Edited for the move.", "See [nothing](/posts/nothing/).")
      let fake = upcomingStore [ "soon", upcomingPost ] [ "soon", broken ]
      unpublish fake "soon" (Some "w1") |> unpublishedOf |> ignore
      Expect.stringContains fake.Working["soon"] "/posts/nothing/" "The draft text stays as it was"
    }

    test "a link from another post to the post is an error, because the link would break" {
      let linking = livePost.Replace("Live body.", "Read [it](/posts/soon/).")
      let fake = upcomingStore [ "soon", upcomingPost; "other", linking ] []

      match unpublish fake "soon" None with
      | Error(ActionError.Logic(Unpublish.BreaksLinks problems)) ->
        Expect.isTrue (problems |> List.exists (fun problem -> problem.Message.Contains "other")) "The problem names the other post"
      | other -> failtestf "Expected BreaksLinks, got %A" other

      Expect.isEmpty fake.Log "No write and no dispatch"
    }

    test "a published blob that changes before the delete is refused, and no row is deleted and no site is built" {
      let fake = upcomingStore [ "soon", upcomingPost ] []
      fake.BeforePublishedWrite <- fun () -> fake.Publish "soon" upcomingPost

      unpublish fake "soon" None |> unpublishRefusedWith (UpcomingGate.Gate PendingGate.PublishedBlobChanged)
      Expect.equal fake.Log [ "working"; "delete published" ] "The steps before the refusal"
      Expect.equal (fake.Rows |> Map.count) 1 "The row stays"
    }

    test "a row delete that fails once is tried again, and the unpublish has no warning" {
      let fake = upcomingStore [ "soon", upcomingPost ] []
      fake.RowFailures <- 1
      let moved = unpublish fake "soon" None |> unpublishedOf
      Expect.isEmpty moved.Warnings "No warning"
      Expect.isEmpty fake.Rows "The row is gone"
    }

    test "a row delete that fails twice is a warning, and the site still builds" {
      let fake = upcomingStore [ "soon", upcomingPost ] []
      fake.RowFailures <- 2
      let moved = unpublish fake "soon" None |> unpublishedOf

      match moved.Warnings with
      | [ PostRowKept _ ] -> ()
      | other -> failtestf "Expected PostRowKept, got %A" other

      Expect.equal (List.last fake.Log) "dispatch" "The dispatch runs"
    }
  ]

[<Tests>]
let changeDateTests =
  testList "Change the date of an upcoming post" [
    test "a date change updates the publish date and the created date of the row" {
      let fake = upcomingStore [ "soon", upcomingPost ] []
      let moved = changeDate fake "soon" None "2026-05-02T08:00:00Z" |> dateChangedOf
      let date = DateTime(2026, 5, 2, 8, 0, 0, DateTimeKind.Utc)

      Expect.equal (Post.summary fake.Published["soon"]).PublishDate (Some(DateTimeOffset date)) "The text has the date"
      Expect.equal fake.Rows["soon"].CreatedDate date "The row has the date"
      Expect.stringContains fake.Published["soon"] "Live body." "The body stays"

      Expect.equal
        moved
        { PublishDate = Some date
          State = TwoPoint.Core.Posts.PostState.Upcoming
          WorkingETag = None
          Production = Dispatched(Queued(RunNumber 7L))
          Warnings = [] }
        "The outcome has the date and the production run"
    }

    test "a date change keeps the migrated mark of the published blob" {
      let fake = upcomingStore [ "soon", upcomingPost ] []
      fake.MarkMigrated "soon"
      changeDate fake "soon" None "2026-05-02T08:00:00Z" |> dateChangedOf |> ignore
      Expect.equal (fake.Mark "soon") Migrated "The mark stays"
    }

    test "a date change writes the published blob, the row, and then dispatches, with no revision" {
      let fake = upcomingStore [ "soon", upcomingPost ] []
      changeDate fake "soon" None "2026-05-02T08:00:00Z" |> dateChangedOf |> ignore
      Expect.equal fake.Log [ "published"; "row"; "dispatch" ] "The steps, in order"
      Expect.isEmpty fake.Revisions "No revision"
    }

    test "a date change moves the publish date of the pending edit, and gives its new ETag" {
      let fake = upcomingStore [ "soon", upcomingPost ] [ "soon", upcomingEdit ]
      let moved = changeDate fake "soon" (Some "w1") "2026-05-02T08:00:00Z" |> dateChangedOf
      let date = DateTime(2026, 5, 2, 8, 0, 0, DateTimeKind.Utc)

      Expect.equal fake.Log [ "published"; "row"; "working"; "dispatch" ] "The steps, in order"
      Expect.equal (Post.summary fake.Working["soon"]).PublishDate (Some(DateTimeOffset date)) "The pending edit has the date"
      Expect.stringContains fake.Working["soon"] "Edited for the move." "The pending edit text stays"
      Expect.equal moved.WorkingETag (Some "w2") "The new ETag of the pending edit"
    }

    test "a new date in the past gives a published post" {
      let fake = upcomingStore [ "soon", upcomingPost ] []
      let moved = changeDate fake "soon" None "2026-02-01T08:00:00Z" |> dateChangedOf

      Expect.equal moved.State TwoPoint.Core.Posts.PostState.Published "Not upcoming"
      Expect.equal fake.Rows["soon"].CreatedDate (DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc)) "The row has the date"
    }

    test "a date change of a post with a past date is refused, and the store does not change" {
      let fake = store [ "live", livePost ] []
      changeDate fake "live" None "2026-05-02T08:00:00Z" |> dateRefusedWith (UpcomingGate.NotUpcoming(slug "live"))
      Expect.isEmpty fake.Log "No write and no dispatch"
    }

    test "a date that is not a UTC time is a validation error before any store read" {
      let fake = upcomingStore [ "soon", upcomingPost ] []

      match changeDate fake "soon" None "next week" with
      | Error(ActionError.Validation _) -> ()
      | other -> failtestf "Expected a validation error, got %A" other

      Expect.isEmpty fake.Log "No write and no dispatch"
    }

    test "an old ETag of the pending edit is refused" {
      let fake = upcomingStore [ "soon", upcomingPost ] [ "soon", upcomingEdit ]
      changeDate fake "soon" (Some "w0") "2026-05-02T08:00:00Z" |> dateRefusedWith (UpcomingGate.Gate PendingGate.WorkingBlobChanged)
      Expect.isEmpty fake.Log "No write"
    }

    test "a published blob that changes before the write is refused, and the row stays" {
      let fake = upcomingStore [ "soon", upcomingPost ] []
      fake.BeforePublishedWrite <- fun () -> fake.Publish "soon" upcomingPost

      changeDate fake "soon" None "2026-05-02T08:00:00Z" |> dateRefusedWith (UpcomingGate.Gate PendingGate.PublishedBlobChanged)
      Expect.equal fake.Rows["soon"].CreatedDate soonDate "The created date stays"
      Expect.isEmpty fake.Log "No dispatch"
    }

    test "a pending edit that changes during the date change stays, with a warning and no ETag" {
      let fake = upcomingStore [ "soon", upcomingPost ] [ "soon", upcomingEdit ]
      fake.BeforePublishedWrite <- fun () -> fake.Overwrite "soon" "newer text"

      let moved = changeDate fake "soon" (Some "w1") "2026-05-02T08:00:00Z" |> dateChangedOf

      Expect.equal moved.Warnings [ Step PublishSteps.NewerTextKept ] "The warning"
      Expect.equal moved.WorkingETag None "No ETag"
      Expect.equal fake.Working["soon"] "newer text" "The newer text stays"
    }

    test "a date change into the past with a row that fails twice gives CommentRowNotWritten" {
      let fake = upcomingStore [ "soon", upcomingPost ] []
      fake.RowFailures <- 2
      let moved = changeDate fake "soon" None "2026-02-01T08:00:00Z" |> dateChangedOf

      Expect.equal moved.State TwoPoint.Core.Posts.PostState.Published "Not upcoming"

      match moved.Warnings with
      | [ Step(PublishSteps.CommentRowNotWritten _) ] -> ()
      | other -> failtestf "Expected CommentRowNotWritten, got %A" other

      Expect.equal fake.Rows["soon"].CreatedDate soonDate "The row keeps the old date"
    }

    test "a pending edit with unreadable front matter keeps its date and gives a warning" {
      let fake = upcomingStore [ "soon", upcomingPost ] [ "soon", "---\ntitle: [\n---\n" ]
      let moved = changeDate fake "soon" (Some "w1") "2026-05-02T08:00:00Z" |> dateChangedOf

      match moved.Warnings with
      | [ PendingDateUnreadable _ ] -> ()
      | other -> failtestf "Expected PendingDateUnreadable, got %A" other

      Expect.equal moved.WorkingETag (Some "w1") "The ETag stays"
    }

    test "a row that fails to write once is tried again, and a second failure is a warning" {
      let fake = upcomingStore [ "soon", upcomingPost ] []
      fake.RowFailures <- 2
      let moved = changeDate fake "soon" None "2026-05-02T08:00:00Z" |> dateChangedOf

      match moved.Warnings with
      | [ Step(PublishSteps.CommentRowNotWritten _) ] -> ()
      | other -> failtestf "Expected CommentRowNotWritten, got %A" other

      Expect.equal (List.last fake.Log) "dispatch" "The dispatch runs"
    }
  ]

let private deletePost (fake : FakeStore) name ifMatch confirm =
  run (DeletePost.withDependencies fake { Slug = name; IfMatch = ifMatch; ConfirmSlug = confirm; Edits = [] })

let private deletedOf result =
  match result with
  | Ok(DeletePost.Event.PostDeleted outcome) -> outcome
  | Error error -> failtestf "Expected a delete, got %A" error

let private deleteRefusedWith expected result =
  match result with
  | Error(ActionError.Logic error) -> Expect.equal error expected "The refusal"
  | other -> failtestf "Expected %A, got %A" expected other

let private deleteWarningsOf outcome =
  match outcome with
  | DeletePost.DraftDeleted warnings -> warnings
  | DeletePost.PublishedDeleted(_, _, warnings) -> warnings
  | DeletePost.PostKept(_, _, _, warnings) -> List.map Step warnings

let private otherPost =
  "---\npublishDate: 2026-01-02T00:00:00Z\nauthor: A\ntitle: Other\nexcerpt: E\ncategory: One-offs\n---\n\nOther body.\n"

let private linkingPost = otherPost.Replace("Other body.", "See [live](/posts/live/).")

let private liveRow : PostRow =
  { Title = NonEmptyString.create (Some "Title") "Live" |> ok
    Slug = slug "live"
    CreatedDate = DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }

let private liveStore () =
  let fake = store [ "live", livePost; "other", otherPost ] [ "live", changedText ]
  fake.AddRevision "live" "2026-01-01T00:00:00Z"
  fake.AddRow liveRow
  fake.AddComments "live" [ "c1"; "c2" ]
  fake.AddComments "other" [ "c3" ]
  fake.AddDeliveries "live" [ "d1" ]
  fake

let private dispatched = ProductionDispatch.Dispatched(Queued(RunNumber 7L))

[<Tests>]
let deleteTests =
  testList "Delete a post" [
    test "a published post loses its blobs, its row, and its comments, keeps its deliveries, and then dispatches" {
      let fake = liveStore ()
      let outcome = deletePost fake "live" (Some "w1") (Some "live") |> deletedOf

      Expect.equal
        fake.Log
        [ "delete published"; "delete working"; "delete revisions"; "delete row"; "delete comments"; "dispatch" ]
        "The row goes before the comments, so that no comment arrives after the delete of the comments"

      Expect.isFalse (fake.Published.ContainsKey "live") "The published blob is gone"
      Expect.isFalse (fake.Working.ContainsKey "live") "The working blob is gone"
      Expect.isEmpty fake.Revisions "No revision"
      Expect.isFalse (fake.Rows.ContainsKey "live") "The row is gone"
      Expect.isFalse (fake.Comments.ContainsKey "live") "The comments are gone"
      Expect.equal fake.Comments["other"] [ "c3" ] "The comments of another post stay"
      Expect.equal fake.Deliveries["live"] [ "d1" ] "The deliveries stay"
      Expect.equal fake.Published["other"] otherPost "Another post stays"
      Expect.equal outcome (DeletePost.PublishedDeleted([], dispatched, [])) "The outcome"
      Expect.equal (fake.Changes |> List.map _.Slug) [ slug "live" ] "The change is recorded"
    }

    test "a published post with no pending edit needs the absence of a working blob" {
      let fake = store [ "live", livePost ] []
      deletePost fake "live" None (Some "live") |> deletedOf |> ignore
      Expect.isEmpty fake.Published "The published blob is gone"
    }

    test "a published post needs its typed slug, and nothing changes without it" {
      for confirm in [ None; Some ""; Some "other"; Some "Live" ] do
        let fake = liveStore ()
        deletePost fake "live" (Some "w1") confirm |> deleteRefusedWith (DeletePost.SlugNotConfirmed(slug "live"))
        Expect.isEmpty fake.Log "No step ran"
    }

    test "a delete that breaks a link in another post is refused, and nothing changes" {
      let fake = store [ "live", livePost; "other", linkingPost ] []

      match deletePost fake "live" None (Some "live") with
      | Error(ActionError.Logic(DeletePost.BreaksLinks problems)) ->
        Expect.isNonEmpty problems "The broken links"
        Expect.isTrue (problems |> List.forall Problem.isError) "Only errors"
        Expect.isTrue (problems |> List.exists (fun problem -> problem.Message.Contains "other")) "The post with the link"
      | other -> failtestf "Expected BreaksLinks, got %A" other

      Expect.isEmpty fake.Log "No step ran"
      Expect.equal fake.Published["live"] livePost "The post stays"
    }

    test "a link that was broken before the delete does not block it" {
      let brokenBefore = otherPost.Replace("Other body.", "See [it](/posts/nowhere/).")
      let fake = store [ "live", livePost; "other", brokenBefore ] []
      deletePost fake "live" None (Some "live") |> deletedOf |> ignore
    }

    test "a published blob that changes between the read and the delete is refused, and nothing is lost" {
      let fake = liveStore ()
      fake.BeforePublishedWrite <- fun () -> fake.Publish "live" "newer"
      deletePost fake "live" (Some "w1") (Some "live") |> deleteRefusedWith DeletePost.PublishedBlobChanged
      Expect.equal fake.Log [ "delete published" ] "Only the refused step"
      Expect.isTrue (fake.Working.ContainsKey "live") "The pending edit stays"
    }

    test "a wrong working ETag, and an absent one for a pending edit, are refused" {
      for ifMatch in [ Some "w0"; None ] do
        let fake = liveStore ()
        deletePost fake "live" ifMatch (Some "live") |> deleteRefusedWith DeletePost.WorkingBlobChanged
        Expect.isEmpty fake.Log "No step ran"
    }

    test "a failed comment delete is a warning, and the dispatch still runs" {
      let fake = liveStore ()
      fake.CommentDeleteFails <- true
      let outcome = deletePost fake "live" (Some "w1") (Some "live") |> deletedOf

      Expect.exists (deleteWarningsOf outcome) (function CommentsKept _ -> true | _ -> false) "The warning"
      Expect.isFalse (fake.Rows.ContainsKey "live") "The row is gone"
      Expect.equal (List.last fake.Log) "dispatch" "The dispatch ran"
    }

    test "a row that stays after two tries is a warning" {
      let fake = liveStore ()
      fake.RowFailures <- 2
      let outcome = deletePost fake "live" (Some "w1") (Some "live") |> deletedOf
      Expect.exists (deleteWarningsOf outcome) (function PostRowKept _ -> true | _ -> false) "The warning"
    }

    test "a failed dispatch is a warning, and the post stays deleted" {
      let fake = liveStore ()
      fake.Dispatch <- WholeCallFails
      let outcome = deletePost fake "live" (Some "w1") (Some "live") |> deletedOf

      Expect.exists (deleteWarningsOf outcome) (function Step(PublishSteps.SiteNotRebuilt _) -> true | _ -> false) "The warning"
      Expect.isFalse (fake.Published.ContainsKey "live") "The post is deleted"
    }

    test "a draft loses its working blob, its revisions, its row, and its comments, and dispatches nothing" {
      let fake = store [] [ "my-draft", draftText ]
      fake.AddRevision "my-draft" "2026-01-01T00:00:00Z"
      fake.AddRow { liveRow with Slug = slug "my-draft" }
      fake.AddComments "my-draft" [ "c1" ]
      let outcome = deletePost fake "my-draft" (Some "w1") None |> deletedOf

      Expect.equal fake.Log [ "delete working"; "delete revisions"; "delete row"; "delete comments" ] "No dispatch"
      Expect.isEmpty fake.Working "The draft is gone"
      Expect.isEmpty fake.Revisions "No revision"
      Expect.isEmpty fake.Rows "No row"
      Expect.isEmpty fake.Comments "No comment"
      Expect.equal outcome (DeletePost.DraftDeleted []) "No dispatch"
      Expect.isEmpty fake.Changes "No change recorded"
    }

    test "a draft needs its working ETag" {
      let fake = store [] [ "my-draft", draftText ]
      deletePost fake "my-draft" None None |> deleteRefusedWith DeletePost.WorkingBlobChanged
      deletePost fake "my-draft" (Some "w0") None |> deleteRefusedWith DeletePost.WorkingBlobChanged
      Expect.isEmpty fake.Log "No step ran"
    }

    test "a post with no blob and a row is cleaned up, and the site rebuilds" {
      let fake = store [] []
      fake.AddRow liveRow
      fake.AddComments "live" [ "c1" ]
      let outcome = deletePost fake "live" None None |> deletedOf

      Expect.isEmpty fake.Rows "No row"
      Expect.isEmpty fake.Comments "No comment"
      Expect.equal (List.last fake.Log) "dispatch" "The dispatch ran"
      Expect.equal outcome (DeletePost.PublishedDeleted([], dispatched, [])) "The outcome"
    }

    test "a post with no blob and comments only is cleaned up" {
      let fake = store [] []
      fake.AddComments "live" [ "c1" ]
      deletePost fake "live" None None |> deletedOf |> ignore
      Expect.isEmpty fake.Comments "No comment"
    }

    test "a post with no blob, no row, and no comment is not found" {
      let fake = store [] []
      deletePost fake "nothing" None None |> deleteRefusedWith (DeletePost.PostNotFound(slug "nothing"))
      Expect.isEmpty fake.Log "No step ran"
    }
  ]

let private thirdPost = linkingPost.Replace("title: Other", "title: Third")
let private fixingEdit = otherPost.Replace("Other body.", "No link now.").Replace("title: Other", "title: Other fixed")
let private thirdFix = fixingEdit.Replace("title: Other fixed", "title: Third fixed")

let private deleteWith (fake : FakeStore) name confirm (edits : (string * string) list) =
  run (
    DeletePost.withDependencies
      fake
      { Slug = name
        IfMatch = None
        ConfirmSlug = confirm
        Edits = edits |> List.map (fun (name, eTag) -> ({ Slug = name; IfMatch = eTag } : RemovalSteps.EditRef)) }
  )

let private checkRemoval (fake : FakeStore) name =
  match run (CheckRemoval.withDependencies fake { Slug = name }) with
  | Ok(CheckRemoval.Event.Checked outcome) -> outcome
  | Error error -> failtestf "Expected a check, got %A" error

let private parsedPost name text =
  Check.published Stage.Staging [] name text |> Result.mapError _.Problems |> ok

let private fix name text problems : RemovalCheck.Fix =
  { Slug = slug name
    Title = "Fixed"
    ETag = "w1"
    Post = Some(parsedPost name text)
    Problems = problems }

let private edit name title : RemovalCheck.Edit = { Slug = slug name; Title = title; ETag = "w1" }

let private blockedProblems outcome =
  match outcome with
  | RemovalCheck.Blocked problems -> problems
  | other -> failtestf "Expected a blocked check, got %A" other

let private mentions (name : string) (problems : Problem list) =
  problems |> List.exists (fun problem -> problem.Message.Contains $"'{name}'")

[<Tests>]
let removalCheckTests =
  let published = [ parsedPost "live" livePost; parsedPost "other" linkingPost; parsedPost "third" thirdPost ]
  let outcome = RemovalCheck.Logic.outcome Stage.Staging

  testList "The removal check" [
    test "a post that no published post links to is a plain removal" {
      Expect.equal (outcome [ parsedPost "live" livePost; parsedPost "other" otherPost ] (slug "live") []) RemovalCheck.PlainRemoval "Plain"
    }

    test "each linking post with a pending edit that removes the link is a publish and remove" {
      Expect.equal
        (outcome published (slug "live") [ fix "other" fixingEdit []; fix "third" thirdFix [] ])
        (RemovalCheck.PublishAndRemove [ edit "other" "Fixed"; edit "third" "Fixed" ])
        "Both edits"
    }

    test "a linking post with no pending edit blocks the removal and is named" {
      let problems = outcome published (slug "live") [ fix "other" fixingEdit [] ] |> blockedProblems
      Expect.isTrue (mentions "third" problems) "The post that keeps a broken link"
      Expect.isFalse (mentions "other" problems) "The fixed post is not named"
      Expect.isTrue (problems |> List.forall Problem.isError) "Only errors"
    }

    test "a pending edit that keeps the link blocks the removal" {
      let problems =
        outcome published (slug "live") [ fix "other" linkingPost []; fix "third" thirdFix [] ] |> blockedProblems

      Expect.isTrue (mentions "other" problems) "The post that keeps the link"
    }

    test "a pending edit with an error in its publish check blocks the removal" {
      let cardError = Problem.error (Place.Field("image", 7)) "The card image is not available."

      let problems =
        outcome published (slug "live") [ fix "other" fixingEdit [ cardError ]; fix "third" thirdFix [] ]
        |> blockedProblems

      Expect.equal
        problems
        [ Problem.error (Place.Post 1) "The pending edit of the post 'other' cannot publish: The card image is not available." ]
        "The error of the edit"
    }

    test "a warning in the publish check of a pending edit does not block the removal" {
      let warning = Problem.warning (Place.Body 1) "A warning."

      Expect.equal
        (outcome published (slug "live") [ fix "other" fixingEdit [ warning ]; fix "third" thirdFix [] ])
        (RemovalCheck.PublishAndRemove [ edit "other" "Fixed"; edit "third" "Fixed" ])
        "Publish and remove"
    }

    test "the pending edit of a post that does not link to the post is not published" {
      let withDetached = published @ [ parsedPost "detached" otherPost ]

      Expect.equal
        (outcome withDetached (slug "live") [ fix "other" fixingEdit []; fix "third" thirdFix []; fix "detached" fixingEdit [] ])
        (RemovalCheck.PublishAndRemove [ edit "other" "Fixed"; edit "third" "Fixed" ])
        "Only the linking posts"
    }
  ]

[<Tests>]
let publishAndDeleteTests =
  testList "Delete a post and publish the pending edits that remove each link to it" [
    test "the check gives the pending edit of each linking post with its title and ETag" {
      let fake = store [ "live", livePost; "other", linkingPost; "detached", otherPost ] [ "other", fixingEdit; "detached", pendingEdit ]

      Expect.equal
        (checkRemoval fake "live")
        (RemovalCheck.PublishAndRemove [ { Slug = slug "other"; Title = "Other fixed"; ETag = "w1" } ])
        "The edit to publish"

      Expect.isEmpty fake.Log "The check writes nothing"
    }

    test "the check of a post that no post links to is a plain removal, and a draft is not published" {
      Expect.equal (checkRemoval (store [ "live", livePost; "other", otherPost ] []) "live") RemovalCheck.PlainRemoval "Plain"

      match run (CheckRemoval.withDependencies (store [] [ "my-draft", draftText ]) { Slug = "my-draft" }) with
      | Error(ActionError.Logic(CheckRemoval.NotPublished _)) -> ()
      | other -> failtestf "Expected NotPublished, got %A" other
    }

    test "the delete publishes the pending edit, deletes the post, and dispatches one time" {
      let fake = store [ "live", livePost; "other", linkingPost; "detached", otherPost ] [ "other", fixingEdit; "detached", pendingEdit ]
      fake.AddComments "live" [ "c1" ]
      let outcome = deleteWith fake "live" (Some "live") [ "other", "w1" ] |> deletedOf

      Expect.equal
        fake.Log
        [ $"revision other/{isoOf utcNow}"
          "published"
          "row"
          "delete working"
          "delete published"
          "delete revisions"
          "delete row"
          "delete comments"
          "dispatch" ]
        "The edit publishes before the delete, and one dispatch ends the action"

      Expect.stringContains fake.Published["other"] "No link now." "The edit is published"
      Expect.stringContains fake.Published["other"] "publishDate: 2026-01-02T00:00:00Z" "The edit keeps the publish date"
      Expect.isFalse (fake.Working.ContainsKey "other") "The pending edit is gone"
      Expect.isFalse (fake.Published.ContainsKey "live") "The post is deleted"
      Expect.equal fake.Working["detached"] pendingEdit "The pending edit of a post with no link stays pending"
      Expect.equal outcome (DeletePost.PublishedDeleted([ slug "other" ], dispatched, [])) "The outcome"

      Expect.equal
        (fake.Changes |> List.map _.Slug |> List.sort)
        [ slug "live"; slug "other" ]
        "A change for the edit and for the delete"

      Expect.equal (fake.Changes |> List.map _.Change |> List.distinct |> List.length) 1 "The same dispatch"
    }

    test "a linking post with no pending edit blocks the delete, and the delete writes nothing" {
      let fake = store [ "live", livePost; "other", linkingPost; "third", thirdPost ] [ "other", fixingEdit ]

      match deleteWith fake "live" (Some "live") [ "other", "w1" ] with
      | Error(ActionError.Logic(DeletePost.BreaksLinks problems)) -> Expect.isTrue (mentions "third" problems) "Names the post"
      | other -> failtestf "Expected BreaksLinks, got %A" other

      Expect.isEmpty fake.Log "No step ran"
      Expect.equal fake.Working["other"] fixingEdit "The edit stays pending"
    }

    test "a pending edit that removes the link and has a bad card image blocks the delete" {
      let withCard = fixingEdit.Replace("category: One-offs", "category: One-offs\nimage: https://example.com/card.jpg")
      let fake = store [ "live", livePost; "other", linkingPost ] [ "other", withCard ]

      fake.Images <-
        { new IImageSize with
            member _.Size _ = Result.Error "HTTP 404" }

      blockedProblems (checkRemoval fake "live") |> mentions "other" |> fun named -> Expect.isTrue named "The check names the post"

      match deleteWith fake "live" (Some "live") [ "other", "w1" ] with
      | Error(ActionError.Logic(DeletePost.BreaksLinks _)) -> ()
      | other -> failtestf "Expected BreaksLinks, got %A" other

      Expect.isEmpty fake.Log "No step ran"
    }

    test "a stale ETag or a different list of edits is refused, and the delete writes nothing" {
      let requests =
        [ [ "other", "w0" ]
          []
          [ "other", "w1"; "detached", "w1" ]
          [ "detached", "w1" ] ]

      for edits in requests do
        let fake = store [ "live", livePost; "other", linkingPost; "detached", otherPost ] [ "other", fixingEdit; "detached", pendingEdit ]
        deleteWith fake "live" (Some "live") edits |> deleteRefusedWith DeletePost.EditsChanged
        Expect.isEmpty fake.Log $"No step ran for {edits}"
    }

    test "a request with edits for a plain delete is refused" {
      let fake = store [ "live", livePost; "other", otherPost ] [ "other", pendingEdit ]
      deleteWith fake "live" (Some "live") [ "other", "w1" ] |> deleteRefusedWith DeletePost.EditsChanged
      Expect.isEmpty fake.Log "No step ran"
    }

    test "a failed published write keeps the post and each edit that published, and still dispatches one time" {
      let fake = store [ "live", livePost; "other", linkingPost; "third", thirdPost ] [ "other", fixingEdit; "third", thirdFix ]
      fake.PublishedWriteFailsFor <- Set.ofList [ "third" ]

      match deleteWith fake "live" (Some "live") [ "other", "w1"; "third", "w1" ] |> deletedOf with
      | DeletePost.PostKept([ edited ], DeletePost.EditNotPublished(failed, PublishSteps.WriteFailed _), production, []) ->
        Expect.equal edited (slug "other") "The published edit"
        Expect.equal failed (slug "third") "The edit that failed"
        Expect.equal production dispatched "The dispatch"
      | other -> failtestf "Expected PostKept, got %A" other

      Expect.equal fake.Published["live"] livePost "The post stays"
      Expect.stringContains fake.Published["other"] "No link now." "The first edit stays published"
      Expect.equal fake.Working["third"] thirdFix "The failed edit stays pending"
      Expect.equal (fake.Log |> List.filter ((=) "dispatch")) [ "dispatch" ] "One dispatch"
      Expect.isFalse (List.contains "delete published" fake.Log) "The post is not deleted"
      Expect.equal (fake.Changes |> List.map _.Slug) [ slug "other" ] "A change for the published edit only"
    }

    test "a failed dispatch after the post is kept is a warning of the kept post" {
      let fake = store [ "live", livePost; "other", linkingPost; "third", thirdPost ] [ "other", fixingEdit; "third", thirdFix ]
      fake.PublishedWriteFailsFor <- Set.ofList [ "third" ]
      fake.Dispatch <- WholeCallFails

      match deleteWith fake "live" (Some "live") [ "other", "w1"; "third", "w1" ] |> deletedOf with
      | DeletePost.PostKept(_, _, _, warnings) ->
        Expect.exists warnings (function PublishSteps.SiteNotRebuilt _ -> true | _ -> false) "The warning"
      | other -> failtestf "Expected PostKept, got %A" other
    }

    test "a failed write of the first edit writes nothing and dispatches nothing" {
      let fake = store [ "live", livePost; "other", linkingPost ] [ "other", fixingEdit ]
      fake.PublishedWriteFailsFor <- Set.ofList [ "other" ]

      match deleteWith fake "live" (Some "live") [ "other", "w1" ] with
      | Error(ActionError.Dependency _) -> ()
      | other -> failtestf "Expected a dependency error, got %A" other

      Expect.isFalse (List.contains "dispatch" fake.Log) "No dispatch"
      Expect.isFalse (List.contains "delete published" fake.Log) "The post is not deleted"
      Expect.isEmpty fake.Revisions "The revision is removed"
    }

    test "a published blob that changes after the edits publish keeps the post, and the edits still dispatch" {
      let fake = store [ "live", livePost; "other", linkingPost ] [ "other", fixingEdit ]
      fake.BeforePublishedWrite <- fun () -> if fake.Published["other"].Contains "No link now." then fake.Publish "live" "newer"

      match deleteWith fake "live" (Some "live") [ "other", "w1" ] |> deletedOf with
      | DeletePost.PostKept([ edited ], DeletePost.PublishedBlobKept, _, _) -> Expect.equal edited (slug "other") "The edit"
      | other -> failtestf "Expected PostKept, got %A" other

      Expect.equal (List.last fake.Log) "dispatch" "The dispatch ran"
    }
  ]

let private linkingSoon = otherPost.Replace("Other body.", "See [soon](/posts/soon/).")
let private thirdSoon = linkingSoon.Replace("title: Other", "title: Third")

let private unpublishWith (fake : FakeStore) name (edits : (string * string) list) =
  run (
    Unpublish.withDependencies
      fake
      { Slug = name
        IfMatch = None
        Edits = edits |> List.map (fun (name, eTag) -> ({ Slug = name; IfMatch = eTag } : RemovalSteps.EditRef)) }
  )

let private keptOf result =
  match result with
  | Ok(Unpublish.Event.PostKept(edits, reason, production, warnings)) -> edits, reason, production, warnings
  | other -> failtestf "Expected a kept post, got %A" other

let private soonWithFix () =
  upcomingStore [ "soon", upcomingPost; "other", linkingSoon; "detached", otherPost ] [ "other", fixingEdit; "detached", pendingEdit ]

let private soonWithTwoFixes () =
  upcomingStore [ "soon", upcomingPost; "other", linkingSoon; "third", thirdSoon ] [ "other", fixingEdit; "third", thirdFix ]

let private expectStaysUpcoming (fake : FakeStore) =
  Expect.equal fake.Published["soon"] upcomingPost "The post stays upcoming"
  Expect.isTrue (fake.Rows.ContainsKey "soon") "The row stays"
  Expect.equal (fake.Log |> List.filter ((=) "dispatch")) [ "dispatch" ] "One dispatch"
  Expect.equal (List.last fake.Log) "dispatch" "The dispatch runs last"
  Expect.equal (fake.Changes |> List.map _.Slug) [ slug "other" ] "A change for the published edit only"

[<Tests>]
let publishAndUnpublishTests =
  testList "Unpublish a post and publish the pending edits that remove each link to it" [
    test "the check of an upcoming post gives the pending edit of each linking post" {
      Expect.equal
        (checkRemoval (soonWithFix ()) "soon")
        (RemovalCheck.PublishAndRemove [ { Slug = slug "other"; Title = "Other fixed"; ETag = "w1" } ])
        "The edit to publish"
    }

    test "the unpublish publishes the pending edit, unpublishes the post, and dispatches one time" {
      let fake = soonWithFix ()
      let moved = unpublishWith fake "soon" [ "other", "w1" ] |> unpublishedOf

      Expect.equal
        fake.Log
        [ $"revision other/{isoOf utcNow}"
          "published"
          "row"
          "delete working"
          "working"
          "delete published"
          "delete row"
          "dispatch" ]
        "The edit publishes before the unpublish, and one dispatch ends the action"

      Expect.stringContains fake.Published["other"] "No link now." "The edit is published"
      Expect.isFalse (fake.Working.ContainsKey "other") "The pending edit is gone"
      Expect.isFalse (fake.Published.ContainsKey "soon") "The post is unpublished"
      Expect.equal (Post.summary fake.Working["soon"]).PublishDate None "The draft has no publish date"
      Expect.isFalse (fake.Rows.ContainsKey "soon") "The row is gone"
      Expect.equal fake.Working["detached"] pendingEdit "The pending edit of a post with no link stays pending"
      Expect.equal moved.State TwoPoint.Core.Posts.PostState.Draft "A draft"
      Expect.equal moved.Production dispatched "The dispatch"
      Expect.isEmpty moved.Warnings "No warning"

      Expect.equal
        (fake.Changes |> List.map _.Slug |> List.sort)
        [ slug "other"; slug "soon" ]
        "A change for the edit and for the unpublish"

      Expect.equal (fake.Changes |> List.map _.Change |> List.distinct |> List.length) 1 "The same dispatch"
    }

    test "a linking post with no pending edit, or with an edit that keeps the link, blocks the unpublish, and nothing changes" {
      for working in [ []; [ "other", linkingSoon ] ] do
        let fake = upcomingStore [ "soon", upcomingPost; "other", linkingSoon ] working

        match unpublishWith fake "soon" (working |> List.map (fun (name, _) -> name, "w1")) with
        | Error(ActionError.Logic(Unpublish.BreaksLinks problems)) -> Expect.isTrue (mentions "other" problems) "Names the post"
        | other -> failtestf "Expected BreaksLinks, got %A" other

        Expect.isEmpty fake.Log "No step ran"
    }

    test "a pending edit that removes the link and has a bad card image blocks the unpublish" {
      let withCard = fixingEdit.Replace("category: One-offs", "category: One-offs\nimage: https://example.com/card.jpg")
      let fake = upcomingStore [ "soon", upcomingPost; "other", linkingSoon ] [ "other", withCard ]

      fake.Images <-
        { new IImageSize with
            member _.Size _ = Result.Error "HTTP 404" }

      match unpublishWith fake "soon" [ "other", "w1" ] with
      | Error(ActionError.Logic(Unpublish.BreaksLinks problems)) -> Expect.isTrue (mentions "other" problems) "Names the post"
      | other -> failtestf "Expected BreaksLinks, got %A" other

      Expect.isEmpty fake.Log "No step ran"
    }

    test "a stale ETag or a different list of edits is refused, and the unpublish writes nothing" {
      for edits in [ [ "other", "w0" ]; []; [ "other", "w1"; "detached", "w1" ]; [ "detached", "w1" ] ] do
        let fake = soonWithFix ()

        match unpublishWith fake "soon" edits with
        | Error(ActionError.Logic Unpublish.EditsChanged) -> ()
        | other -> failtestf "Expected EditsChanged for %A, got %A" edits other

        Expect.isEmpty fake.Log $"No step ran for {edits}"
    }

    test "a request with edits for a plain removal is refused" {
      let fake = upcomingStore [ "soon", upcomingPost; "other", otherPost ] [ "other", pendingEdit ]

      match unpublishWith fake "soon" [ "other", "w1" ] with
      | Error(ActionError.Logic Unpublish.EditsChanged) -> ()
      | other -> failtestf "Expected EditsChanged, got %A" other

      Expect.isEmpty fake.Log "No step ran"
    }

    test "a failed write of the first edit writes nothing and dispatches nothing" {
      let fake = soonWithFix ()
      fake.PublishedWriteFailsFor <- Set.ofList [ "other" ]

      match unpublishWith fake "soon" [ "other", "w1" ] with
      | Error(ActionError.Dependency _) -> ()
      | other -> failtestf "Expected a dependency error, got %A" other

      Expect.isFalse (List.contains "dispatch" fake.Log) "No dispatch"
      Expect.isFalse (List.contains "working" fake.Log) "No draft text"
      Expect.equal fake.Published["soon"] upcomingPost "The post stays upcoming"
      Expect.isEmpty fake.Revisions "The revision is removed"
    }

    test "a failed second edit keeps the post upcoming and the first edit published, and still dispatches one time" {
      let fake = soonWithTwoFixes ()
      fake.PublishedWriteFailsFor <- Set.ofList [ "third" ]

      match unpublishWith fake "soon" [ "other", "w1"; "third", "w1" ] |> keptOf with
      | [ edited ], Unpublish.EditNotPublished(failed, PublishSteps.WriteFailed _), production, [] ->
        Expect.equal edited (slug "other") "The published edit"
        Expect.equal failed (slug "third") "The edit that failed"
        Expect.equal production dispatched "The dispatch"
      | other -> failtestf "Expected EditNotPublished, got %A" other

      expectStaysUpcoming fake
      Expect.stringContains fake.Published["other"] "No link now." "The first edit stays published"
      Expect.equal fake.Working["third"] thirdFix "The failed edit stays pending"
      Expect.isFalse (fake.Working.ContainsKey "soon") "No draft text"
    }

    test "a failed write of the draft text after the edits keeps the post upcoming, and still dispatches" {
      let fake = soonWithFix ()
      fake.WorkingWriteFails <- true

      match unpublishWith fake "soon" [ "other", "w1" ] |> keptOf with
      | [ edited ], Unpublish.WorkingWriteFailed _, _, _ -> Expect.equal edited (slug "other") "The published edit"
      | other -> failtestf "Expected WorkingWriteFailed, got %A" other

      expectStaysUpcoming fake
      Expect.isFalse (List.contains "delete published" fake.Log) "The published blob stays"
    }

    test "a working blob that another tab saves during the unpublish keeps the post upcoming" {
      let fake = soonWithFix ()
      fake.BeforePublishedWrite <- fun () -> if not (fake.Working.ContainsKey "soon") then fake.Overwrite "soon" "newer"

      match unpublishWith fake "soon" [ "other", "w1" ] |> keptOf with
      | [ _ ], Unpublish.WorkingBlobKept, _, _ -> ()
      | other -> failtestf "Expected WorkingBlobKept, got %A" other

      expectStaysUpcoming fake
      Expect.equal fake.Working["soon"] "newer" "The newer text stays"
    }

    test "a failed delete of the published blob after the edits keeps the post upcoming, and still dispatches" {
      let fake = soonWithFix ()
      fake.PublishedDeleteFails <- true

      match unpublishWith fake "soon" [ "other", "w1" ] |> keptOf with
      | [ edited ], Unpublish.PublishedDeleteFailed _, _, _ -> Expect.equal edited (slug "other") "The published edit"
      | other -> failtestf "Expected PublishedDeleteFailed, got %A" other

      expectStaysUpcoming fake
    }

    test "a published blob that changes after the edits publish keeps the post upcoming" {
      let fake = soonWithFix ()
      fake.BeforePublishedWrite <- fun () -> if fake.Published["other"].Contains "No link now." then fake.Publish "soon" upcomingPost

      match unpublishWith fake "soon" [ "other", "w1" ] |> keptOf with
      | [ _ ], Unpublish.PublishedBlobKept, _, _ -> ()
      | other -> failtestf "Expected PublishedBlobKept, got %A" other

      expectStaysUpcoming fake
    }
  ]
