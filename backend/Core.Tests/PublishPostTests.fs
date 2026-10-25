module TwoPoint.Core.Tests.PublishPostTests

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

type private FakeStore(published : Map<string, string>, working : Map<string, string>) =
  let log = ResizeArray<string>()
  let mutable published = published
  let mutable working = working
  let mutable version = 1
  let mutable revisions = Map.empty<string, string>
  let mutable rows = Map.empty<string, NewPost>
  let mutable images : IImageSize = okImages
  let mutable rowFails = false
  let mutable rebuildFails = false
  let mutable beforeDelete = ignore

  member _.Log = List.ofSeq log
  member _.Published = published
  member _.Working = working
  member _.Revisions = revisions
  member _.Rows = rows

  member _.Images with set value = images <- value
  member _.RowFails with set value = rowFails <- value
  member _.RebuildFails with set value = rebuildFails <- value
  member _.BeforeDelete with set action = beforeDelete <- action

  member _.Overwrite (name : string) (text : string) =
    version <- version + 1
    working <- working |> Map.add name text

  interface PublishPost.Dependencies with
    member _.Stage = Stage.Staging
    member _.Images = images
    member _.GetUtcNow () = utcNow
    member _.ParsePublished = PostCheck.parsePublished Stage.Staging

    member _.GetPublishedBlob slug =
      published |> Map.tryFind (Slug.value slug) |> Option.map (fun text -> { Text = text; ETag = "p1" }) |> CancellableTaskResult.singleton

    member _.GetWorkingBlob slug =
      working |> Map.tryFind (Slug.value slug) |> Option.map (fun text -> { Text = text; ETag = $"w{version}" }) |> CancellableTaskResult.singleton

    member _.GetPublishedPosts () =
      published
      |> Map.toList
      |> List.map (fun (name, text) -> { StoredPost.Slug = slug name; Text = text; ETag = "p1" })
      |> CancellableTaskResult.singleton

    member _.GetSeriesRecords () = CancellableTaskResult.singleton []

    member _.WriteRevisionBlob slug time text =
      log.Add $"revision {Slug.value slug}/{time}"
      revisions <- revisions |> Map.add $"{Slug.value slug}/{time}" text
      CancellableTaskResult.singleton ()

    member _.WritePublishedBlob slug text condition =
      match condition, published |> Map.tryFind (Slug.value slug) with
      | MustNotExist, None ->
        log.Add "published"
        published <- published |> Map.add (Slug.value slug) text
        CancellableTaskResult.singleton (Written "p1")
      | _ -> CancellableTaskResult.singleton ConditionFailed

    member _.UpsertPostRow post =
      log.Add "row"

      if rowFails then
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

    member _.RebuildSite () =
      log.Add "rebuild"

      if rebuildFails then
        CancellableTaskResult.error (down "GitHub")
      else
        CancellableTaskResult.singleton ()

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

let private commentOnPostsOf (fake : FakeStore) =
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
        member _.GetCommenterByValidationId _ = CancellableTaskResult.singleton (Some commenter)
        member _.CreateComment _ commenter newComment approval =
          let posted =
            Comment.create
              "0b6c1f0e-2f0a-4c8e-9a51-6d7c2b1e3f40"
              "2026-03-02T00:00:00.0000000Z"
              {| Approval = approval.ToString(); CreatedDate = "2026-03-02T00:00:00.0000000Z" |}
              {| ValidationId = commenterId
                 EmailAddress = "commenter@example.com"
                 Name = Some "Commenter"
                 CreatedDate = "2026-01-01T00:00:00.0000000Z"
                 Status = "new" |}
              "A comment"
            |> ok

          CancellableTaskResult.singleton { posted with Commenter = commenter; Content = newComment.Content }
        member _.QueueNewCommentNotice _ = CancellableTaskResult.singleton () }

  let dto =
    { NewComment.Dto.Post = NewComment.Post "my-draft"
      NewComment.Dto.ValidationId = NewComment.ValidationId commenterId
      NewComment.Dto.Comment = NewComment.Comment "A comment" }

  (PostComment.withDependencies dependencies dto CancellationToken.None).Result

[<Tests>]
let tests =
  testList "Publish a draft" [
    test "a publish writes the revision, the published blob, the row, and deletes the working blob, and then dispatches" {
      let fake = store [] [ "my-draft", draftText ]
      let outcome = publish fake "my-draft" (Some "w1") Now |> outcomeOf

      Expect.equal
        fake.Log
        [ "revision my-draft/2026-03-01T09:30:45Z"; "published"; "row"; "delete working"; "rebuild" ]
        "The steps of the spec, in order"

      Expect.equal
        outcome
        { PublishDate = DateTime(2026, 3, 1, 9, 30, 45, DateTimeKind.Utc)
          State = TwoPoint.Core.Posts.PostState.Published
          Warnings = [] }
        "The outcome"

      Expect.isEmpty fake.Working "The working blob is gone"
    }

    test "the published blob and the revision contain the text with the publish date, and the other fields stay" {
      let fake = store [] [ "my-draft", taggedText ]
      publish fake "my-draft" (Some "w1") Now |> ignore

      let text = fake.Published["my-draft"]
      let publishTime = DateTimeOffset(DateTime(2026, 3, 1, 9, 30, 45, DateTimeKind.Utc))
      Expect.equal (Post.summary text).PublishDate (Some publishTime) "The publish date is the publish time"
      Expect.equal (Post.summary text).Title (Some "My draft") "The title"
      Expect.stringContains text "FSharp" "The tags stay"
      Expect.equal fake.Revisions["my-draft/2026-03-01T09:30:45Z"] text "The revision is a copy"
    }

    test "the row has the title and the publish date" {
      let fake = store [] [ "my-draft", draftText ]
      publish fake "my-draft" (Some "w1") Now |> ignore

      let row = fake.Rows["my-draft"]
      Expect.equal (NonEmptyString.value row.Title) "My draft" "The title"
      Expect.equal row.CreatedDate (DateTime(2026, 3, 1, 9, 30, 45, DateTimeKind.Utc)) "The created date"
    }

    test "a publish with a future date is upcoming and has that date in the text, the revision, and the row" {
      let fake = store [] [ "my-draft", draftText ]
      let outcome = publish fake "my-draft" (Some "w1") (On "2026-04-01T13:00:00Z") |> outcomeOf
      let date = DateTime(2026, 4, 1, 13, 0, 0, DateTimeKind.Utc)

      Expect.equal outcome.State TwoPoint.Core.Posts.PostState.Upcoming "An upcoming post"
      Expect.equal fake.Rows["my-draft"].CreatedDate date "The row has the date"
      Expect.contains (fake.Revisions |> Map.keys |> List.ofSeq) "my-draft/2026-04-01T13:00:00Z" "The revision has the date"
      Expect.equal (Post.summary fake.Published["my-draft"]).PublishDate (Some(DateTimeOffset date)) "The text has the date"
    }

    test "a publish with a past date is refused, and the store does not change" {
      let fake = store [] [ "my-draft", draftText ]

      match publish fake "my-draft" (Some "w1") (On "2026-02-01T13:00:00Z") with
      | Error(ActionError.Logic(PublishPost.Refused PublishGate.DateInPast)) -> ()
      | other -> failtestf "Expected DateInPast, got %A" other

      Expect.isEmpty fake.Log "No write and no dispatch"
    }

    test "a publish date that is not a UTC time is a validation error" {
      let fake = store [] [ "my-draft", draftText ]

      match publish fake "my-draft" (Some "w1") (On "next week") with
      | Error(ActionError.Validation _) -> ()
      | other -> failtestf "Expected a validation error, got %A" other

      Expect.isEmpty fake.Log "No write and no dispatch"
    }

    test "a post with an error is refused, and the store does not change" {
      let broken = draftText.Replace("A body.", "See [it](/posts/nowhere/).")
      let fake = store [] [ "my-draft", broken ]

      match publish fake "my-draft" (Some "w1") Now with
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

      match publish fake "my-draft" (Some "w1") Now with
      | Error(ActionError.Logic(PublishPost.HasErrors problems)) ->
        Expect.equal
          (problems |> List.map _.Message)
          [ $"The front matter gives the Open Graph image {image}, and the build cannot make its crop: HTTP 404." ]
          "The message of the build"
      | other -> failtestf "Expected HasErrors, got %A" other

      Expect.isEmpty fake.Log "No write and no dispatch"
    }

    test "a warning does not block the publish" {
      let fake = store [] [ "my-draft", taggedText ]

      match check fake "my-draft" (Some "w1") Now with
      | Ok(CheckPublish.Event.Checked problems) ->
        Expect.isNonEmpty problems "The post has the warning for the year tag"
        Expect.isFalse (problems |> List.exists Problem.isError) "No error"
      | Error error -> failtestf "Expected a check, got %A" error

      Expect.equal (publish fake "my-draft" (Some "w1") Now |> outcomeOf).Warnings [] "Published"
    }

    test "the check gives the problems and writes nothing" {
      let broken = draftText.Replace("A body.", "See [it](/posts/nowhere/).")
      let fake = store [] [ "my-draft", broken ]

      match check fake "my-draft" (Some "w1") Now with
      | Ok(CheckPublish.Event.Checked problems) -> Expect.isTrue (problems |> List.exists Problem.isError) "The error"
      | Error error -> failtestf "Expected a check, got %A" error

      Expect.isEmpty fake.Log "No write"
    }

    test "the check of a past date is refused" {
      let fake = store [] [ "my-draft", draftText ]

      match check fake "my-draft" (Some "w1") (On "2026-02-01T13:00:00Z") with
      | Error(ActionError.Logic(CheckPublish.Refused PublishGate.DateInPast)) -> ()
      | other -> failtestf "Expected DateInPast, got %A" other
    }

    test "a failed dispatch leaves the post published and gives a warning" {
      let fake = store [] [ "my-draft", draftText ]
      fake.RebuildFails <- true

      match (publish fake "my-draft" (Some "w1") Now |> outcomeOf).Warnings with
      | [ PublishPost.SiteNotRebuilt _ ] -> ()
      | other -> failtestf "Expected SiteNotRebuilt, got %A" other

      Expect.isTrue (fake.Published.ContainsKey "my-draft") "The published blob stays"
      Expect.isTrue (fake.Rows.ContainsKey "my-draft") "The row stays"
      Expect.isEmpty fake.Working "The working blob is gone"
    }

    test "a failed row write gives a warning, and the later steps still run" {
      let fake = store [] [ "my-draft", draftText ]
      fake.RowFails <- true

      match (publish fake "my-draft" (Some "w1") Now |> outcomeOf).Warnings with
      | [ PublishPost.CommentRowNotWritten _ ] -> ()
      | other -> failtestf "Expected CommentRowNotWritten, got %A" other

      Expect.equal (List.rev fake.Log |> List.take 2) [ "rebuild"; "delete working" ] "The delete and the dispatch ran"
      Expect.isTrue (fake.Published.ContainsKey "my-draft") "The published blob stays"
    }

    test "a working blob that changes during the publish stays as the pending edit, with no warning" {
      let fake = store [] [ "my-draft", draftText ]
      fake.BeforeDelete <- fun () -> fake.Overwrite "my-draft" "newer text"

      Expect.equal (publish fake "my-draft" (Some "w1") Now |> outcomeOf).Warnings [] "No warning"
      Expect.equal fake.Working["my-draft"] "newer text" "The newer text stays"
    }

    test "a publish with an old ETag is refused, and the store does not change" {
      let fake = store [] [ "my-draft", draftText ]

      match publish fake "my-draft" (Some "w0") Now with
      | Error(ActionError.Logic(PublishPost.Refused PublishGate.Stale)) -> ()
      | other -> failtestf "Expected Stale, got %A" other

      Expect.isEmpty fake.Log "No write"
    }

    test "a publish of a post that is published is refused, and the published blob stays" {
      let fake = store [ "live", livePost ] [ "live", pendingEdit ]

      match publish fake "live" (Some "w1") Now with
      | Error(ActionError.Logic(PublishPost.Refused(PublishGate.AlreadyPublished _))) -> ()
      | other -> failtestf "Expected AlreadyPublished, got %A" other

      Expect.isEmpty fake.Log "No write"
      Expect.equal fake.Published["live"] livePost "The published text stays"
    }

    test "a publish of a post with no working blob is refused" {
      let fake = store [] []

      match publish fake "nothing" (Some "w1") Now with
      | Error(ActionError.Logic(PublishPost.Refused(PublishGate.PostNotFound _))) -> ()
      | other -> failtestf "Expected PostNotFound, got %A" other
    }

    test "a comment on a new post succeeds after its publish date with no manual row" {
      let fake = store [] [ "my-draft", draftText ]
      publish fake "my-draft" (Some "w1") Now |> outcomeOf |> ignore

      match commentOnPostsOf fake with
      | Ok(PostComment.Event.Posted _) -> ()
      | other -> failtestf "Expected Posted, got %A" other
    }
  ]
