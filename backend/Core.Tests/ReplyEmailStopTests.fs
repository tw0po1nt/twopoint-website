module TwoPoint.Core.Tests.ReplyEmailStopTests

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Posts.Queries
open TwoPoint.Core.Shared
open TwoPoint.Core.Tests.Fixtures
open TwoPoint.Core.Util

open Expecto
open FsToolkit.ErrorHandling

open System
open System.Threading

let private now = DateTime(2026, 3, 1, 9, 30, 0, DateTimeKind.Utc)
let private consentDate = DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
let private token = Guid "4e3d2c1b-0a9f-4e8d-8c7b-6a5f4e3d2c1b"
let private otherToken = Guid "9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d"
let private commentId = "0b6c1f0e-2f0a-4c8e-9a51-6d7c2b1e3f40"
let private otherCommentId = "2f1e0d9c-8b7a-4f6e-9d5c-4b3a2f1e0d9c"
let private commenterId = "7d1f0c55-3c1b-4a43-9a0e-2b5f6f0d8e21"

let private commenter =
  Commenter.create commenterId "commenter@example.com" (Some "Ada") "2026-01-01T00:00:00.0000000Z" "trusted"
  |> ok

let private consent (stopToken : Guid) (stopDate : DateTime option) =
  { ConsentDate = consentDate; StopToken = ReplyEmailStopToken.ofGuid stopToken; StopDate = stopDate }

let private postedComment (id : string) (replyEmails : ReplyEmailConsent option) =
  let createdDate = "2026-02-01T00:00:00.0000000Z"

  let comment =
    Comment.create
      id
      createdDate
      {| Approval = "approved"; CreatedDate = createdDate |}
      None
      (CommentWriter.Commenter commenter)
      replyEmails
      "A question"
    |> ok

  { Post = olderPost.Id; PostTitle = olderPost.Info.Title; Comment = comment }

let private stopRow (stopToken : Guid) (id : string) =
  { Token = ReplyEmailStopToken.ofGuid stopToken
    Post = olderPost.Id
    Comment = CommentId.create id |> ok }

type private FakeStops(stops : ReplyEmailStop list, comments : PostedComment list) =
  let mutable comments = comments
  let writes = ResizeArray<CommentId * ReplyEmailConsent>()

  member _.Writes = List.ofSeq writes

  interface StopReplyEmails.Dependencies with
    member _.GetReplyEmailStop token =
      stops |> List.tryFind (fun stop -> stop.Token = token) |> CancellableTaskResult.singleton
    member _.GetPostedComment post id =
      comments |> List.tryFind (fun posted -> posted.Post = post && posted.Comment.Id = id) |> CancellableTaskResult.singleton
    member _.GetUtcNow () = now
    member _.UpdateReplyEmailConsent post comment consent =
      Expect.equal post olderPost.Id "Expected the post of the comment"
      writes.Add (comment, consent)

      comments <-
        comments
        |> List.map (fun posted ->
          if posted.Comment.Id = comment then
            { posted with Comment = { posted.Comment with ReplyEmails = Some consent } }
          else
            posted)

      CancellableTaskResult.singleton ()

let private fakeWith (replyEmails : ReplyEmailConsent option) =
  FakeStops([ stopRow token commentId ], [ postedComment commentId replyEmails ])

let private read (dependencies : FakeStops) (token : string) =
  (GetReplyEmailStop.withDependencies dependencies token CancellationToken.None).Result

let private stop (dependencies : FakeStops) (token : string) =
  (StopReplyEmails.withDependencies dependencies token CancellationToken.None).Result

let private title = olderPost.Info.Title

[<Tests>]
let tests =
  testList "the reply-email stop" [
    test "a consent with no stop date is active, with the post title" {
      let dependencies = fakeWith (Some (consent token None))
      Expect.equal (read dependencies (string token)) (Ok (ReplyEmailStopState.Active title)) "Expected an active token"
    }

    test "a consent with a stop date is stopped, with the post title" {
      let dependencies = fakeWith (Some (consent token (Some consentDate)))
      Expect.equal (read dependencies (string token)) (Ok (ReplyEmailStopState.Stopped title)) "Expected a stopped token"
    }

    test "a token with no stop row is unknown" {
      let dependencies = fakeWith (Some (consent token None))
      Expect.equal (read dependencies (string otherToken)) (Ok ReplyEmailStopState.Unknown) "Expected an unknown token"
    }

    test "a token that is not a GUID is unknown" {
      let dependencies = fakeWith (Some (consent token None))

      for text in [ ""; "not-a-token" ] do
        Expect.equal (read dependencies text) (Ok ReplyEmailStopState.Unknown) $"Expected '{text}' to be unknown"
    }

    test "the token of a deleted comment is unknown" {
      let dependencies = FakeStops([ stopRow token commentId ], [])
      Expect.equal (read dependencies (string token)) (Ok ReplyEmailStopState.Unknown) "Expected an unknown token"
    }

    test "a comment with no consent, or with the consent of another token, is unknown" {
      for replyEmails in [ None; Some (consent otherToken None) ] do
        let dependencies = fakeWith replyEmails
        Expect.equal (read dependencies (string token)) (Ok ReplyEmailStopState.Unknown) $"Expected unknown for %A{replyEmails}"
    }

    test "a stop of an active token writes the stop date and answers as stopped" {
      let dependencies = fakeWith (Some (consent token None))

      Expect.equal (stop dependencies (string token)) (Ok (ReplyEmailStopState.Stopped title)) "Expected a stopped token"

      Expect.equal
        dependencies.Writes
        [ CommentId.create commentId |> ok, consent token (Some now) ]
        "Expected the stop date on the consent of the comment"
    }

    test "a second stop answers as stopped and writes nothing" {
      let dependencies = fakeWith (Some (consent token None))

      Expect.equal (stop dependencies (string token)) (Ok (ReplyEmailStopState.Stopped title)) "Expected the first stop"
      Expect.equal (stop dependencies (string token)) (Ok (ReplyEmailStopState.Stopped title)) "Expected the second stop"
      Expect.equal dependencies.Writes.Length 1 "Expected only the write of the first stop"
      Expect.equal (read dependencies (string token)) (Ok (ReplyEmailStopState.Stopped title)) "Expected a stopped token"
    }

    test "a stop of a stopped token keeps the first stop date" {
      let dependencies = fakeWith (Some (consent token (Some consentDate)))

      Expect.equal (stop dependencies (string token)) (Ok (ReplyEmailStopState.Stopped title)) "Expected a stopped token"
      Expect.isEmpty dependencies.Writes "Expected no write"
    }

    test "a stop of an unknown token answers as unknown and writes nothing" {
      let dependencies = FakeStops([ stopRow token commentId ], [])

      for text in [ string token; string otherToken; "not-a-token" ] do
        Expect.equal (stop dependencies text) (Ok ReplyEmailStopState.Unknown) $"Expected '{text}' to be unknown"

      Expect.isEmpty dependencies.Writes "Expected no write"
    }

    test "a stop applies only to the comment of the token" {
      let dependencies =
        FakeStops(
          [ stopRow token commentId; stopRow otherToken otherCommentId ],
          [ postedComment commentId (Some (consent token None))
            postedComment otherCommentId (Some (consent otherToken None)) ]
        )

      Expect.equal (stop dependencies (string token)) (Ok (ReplyEmailStopState.Stopped title)) "Expected a stopped token"
      Expect.equal (dependencies.Writes |> List.map fst) [ CommentId.create commentId |> ok ] "Expected one write, for the comment of the token"
      Expect.equal (read dependencies (string otherToken)) (Ok (ReplyEmailStopState.Active title)) "Expected the other comment to stay active"
    }
  ]
