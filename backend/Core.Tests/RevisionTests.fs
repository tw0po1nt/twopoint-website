module TwoPoint.Core.Tests.RevisionTests

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Util

open Expecto
open FsToolkit.ErrorHandling
open IcedTasks

open System
open System.Threading

let private livePost =
  "---\npublishDate: 2026-01-01T00:00:00Z\nauthor: A\ntitle: Live\nexcerpt: E\ncategory: One-offs\n---\n\nLive body.\n"

let private pendingEdit =
  "---\npublishDate: 2026-01-01T00:00:00Z\nauthor: A\ntitle: Live\nexcerpt: E\ncategory: One-offs\n---\n\nEdited.\n"

let private firstText =
  "---\npublishDate: 2026-01-01T00:00:00Z\nauthor: A\ntitle: First title\nexcerpt: E\ncategory: One-offs\n---\n\nFirst body.\n"

let private draftText = "---\nauthor: A\ntitle: My draft\nexcerpt: E\ncategory: One-offs\n---\n\nA body.\n"

let private firstName = "2026-01-01T00:00:00.0000000Z"
let private secondName = "2026-02-01T10:00:00Z"

let private name value =
  match RevisionName.create value with
  | Ok name -> name
  | Error reason -> failtestf "Not a revision name: %A" reason

module GetRevisionsQuery = TwoPoint.Core.Posts.Queries.GetRevisions

type private FakeStore(published : Map<string, string>, working : Map<string, string>, revisions : Map<string, string>) =
  let log = ResizeArray<string>()
  let mutable published = published
  let mutable working = working
  let mutable version = 1

  member _.Log = List.ofSeq log
  member _.Published = published
  member _.Working = working

  interface GetRevisionsQuery.Dependencies with
    member _.GetRevisionBlobs slug =
      revisions
      |> Map.toList
      |> List.choose (fun (key, text) ->
        match key.Split '/' with
        | [| post; revision |] when post = Slug.value slug -> Some { Name = name revision; Text = text }
        | _ -> None)
      |> CancellableTaskResult.singleton

  interface RestoreRevision.Dependencies with
    member _.GetPublishedBlob slug =
      published |> Map.tryFind (Slug.value slug) |> Option.map (fun text -> { Text = text; ETag = "p1" }) |> CancellableTaskResult.singleton

    member _.GetWorkingBlob slug =
      working |> Map.tryFind (Slug.value slug) |> Option.map (fun text -> { Text = text; ETag = $"w{version}" }) |> CancellableTaskResult.singleton

    member _.GetRevisionBlob slug revision =
      revisions |> Map.tryFind $"{Slug.value slug}/{RevisionName.value revision}" |> CancellableTaskResult.singleton

    member _.WriteWorkingBlob slug text condition =
      let key = Slug.value slug

      let allowed =
        match condition, working |> Map.tryFind key with
        | MustNotExist, None -> true
        | MatchesETag eTag, Some _ -> eTag = $"w{version}"
        | _ -> false

      if allowed then
        log.Add "working"
        version <- version + 1
        working <- working |> Map.add key text
        CancellableTaskResult.singleton (Written $"w{version}")
      else
        CancellableTaskResult.singleton ConditionFailed


let private store published working =
  FakeStore(Map.ofList published, Map.ofList working, Map.ofList [ $"live/{firstName}", firstText; $"live/{secondName}", livePost ])

let private run (task : CancellableTask<'a>) = (task CancellationToken.None).Result

let private restore (fake : FakeStore) slug revision ifMatch =
  run (RestoreRevision.withDependencies fake { Slug = slug; Revision = revision; IfMatch = ifMatch })

let private list (fake : FakeStore) slug =
  run (GetRevisionsQuery.withDependencies fake slug)

[<Tests>]
let tests =
  testList "Revisions" [
    test "the list has each revision of the post, the newest first, with its time and title" {
      match list (store [ "live", livePost ] []) "live" with
      | Ok revisions ->
        Expect.equal
          (revisions |> List.map (fun revision -> RevisionName.value revision.Name, revision.PublishedAt, revision.Title))
          [ secondName, DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc), Some "Live"
            firstName, DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Some "First title" ]
          "The revisions"
      | Error error -> failtestf "Expected the revisions, got %A" error
    }

    test "a post with no revision has an empty list" {
      match list (store [] [ "my-draft", draftText ]) "my-draft" with
      | Ok revisions -> Expect.isEmpty revisions "No revision"
      | Error error -> failtestf "Expected an empty list, got %A" error
    }

    test "a restore writes the revision content to the working blob and changes no published blob" {
      let fake = store [ "live", livePost ] []

      match restore fake "live" firstName None with
      | Ok(RestoreRevision.Restored eTag) -> Expect.equal eTag "w2" "The ETag of the new working blob"
      | other -> failtestf "Expected Restored, got %A" other

      Expect.equal fake.Log [ "working" ] "Only the working write"
      Expect.equal fake.Working["live"] firstText "The pending edit is the revision"
      Expect.equal fake.Published["live"] livePost "The published text stays"
    }

    test "a restore replaces the pending edit that the editor read" {
      let fake = store [ "live", livePost ] [ "live", pendingEdit ]

      match restore fake "live" firstName (Some "w1") with
      | Ok(RestoreRevision.Restored _) -> ()
      | other -> failtestf "Expected Restored, got %A" other

      Expect.equal fake.Working["live"] firstText "The pending edit is the revision"
      Expect.equal fake.Published["live"] livePost "The published text stays"
    }

    test "a restore over a pending edit that changed is refused, and the newer text stays" {
      let fake = store [ "live", livePost ] [ "live", pendingEdit ]

      match restore fake "live" firstName (Some "w0") with
      | Error(ActionError.Logic RestoreRevision.Stale) -> ()
      | other -> failtestf "Expected Stale, got %A" other

      Expect.isEmpty fake.Log "No write"
      Expect.equal fake.Working["live"] pendingEdit "The pending edit stays"
    }

    test "a restore with an ETag when the post has no pending edit is refused" {
      let fake = store [ "live", livePost ] []

      match restore fake "live" firstName (Some "w1") with
      | Error(ActionError.Logic RestoreRevision.Stale) -> ()
      | other -> failtestf "Expected Stale, got %A" other

      Expect.isEmpty fake.Working "No pending edit"
    }

    test "a restore with no ETag when the post has a pending edit is refused" {
      let fake = store [ "live", livePost ] [ "live", pendingEdit ]

      match restore fake "live" firstName None with
      | Error(ActionError.Logic RestoreRevision.Stale) -> ()
      | other -> failtestf "Expected Stale, got %A" other

      Expect.equal fake.Working["live"] pendingEdit "The pending edit stays"
    }

    test "a revision that does not exist is refused" {
      let fake = store [ "live", livePost ] []

      match restore fake "live" "2025-05-05T00:00:00Z" None with
      | Error(ActionError.Logic(RestoreRevision.RevisionNotFound _)) -> ()
      | other -> failtestf "Expected RevisionNotFound, got %A" other

      Expect.isEmpty fake.Log "No write"
    }

    test "a draft is refused, so the draft is not replaced" {
      let fake = store [] [ "live", draftText ]

      match restore fake "live" firstName (Some "w1") with
      | Error(ActionError.Logic(RestoreRevision.NotPublished _)) -> ()
      | other -> failtestf "Expected NotPublished, got %A" other

      Expect.equal fake.Working["live"] draftText "The draft stays"
    }

    test "a post with no blob is refused" {
      match restore (store [] []) "live" firstName None with
      | Error(ActionError.Logic(RestoreRevision.PostNotFound _)) -> ()
      | other -> failtestf "Expected PostNotFound, got %A" other
    }

    test "a revision name that is not a UTC time is refused" {
      match restore (store [ "live", livePost ] []) "live" "../other/2026-01-01T00:00:00Z" None with
      | Error(ActionError.Validation _) -> ()
      | other -> failtestf "Expected a validation error, got %A" other
    }

    test "a revision name with a line break after the time is refused" {
      match RevisionName.create $"{secondName}\n" with
      | Error _ -> ()
      | Ok name -> failtestf "Expected a refusal, got %A" name
    }
  ]
