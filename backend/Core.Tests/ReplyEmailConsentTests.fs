module TwoPoint.Core.Tests.ReplyEmailConsentTests

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Shared
open TwoPoint.Core.Tests.Fixtures
open TwoPoint.Core.Util

open Expecto
open FsToolkit.ErrorHandling

open System
open System.Threading

let private now = DateTime(2026, 3, 1, 9, 30, 0, DateTimeKind.Utc)
let private token = Guid "4e3d2c1b-0a9f-4e8d-8c7b-6a5f4e3d2c1b"
let private newId = "2f1e0d9c-8b7a-4f6e-9d5c-4b3a2f1e0d9c"
let private topLevelId = "0b6c1f0e-2f0a-4c8e-9a51-6d7c2b1e3f40"
let private commenterId = "7d1f0c55-3c1b-4a43-9a0e-2b5f6f0d8e21"

let private commenter (status : CommenterStatus) =
  Commenter.create commenterId "commenter@example.com" (Some "Ada") "2026-01-01T00:00:00.0000000Z" (status.ToString())
  |> ok

let private topLevel =
  let createdDate = "2026-02-01T00:00:00.0000000Z"

  Comment.create
    topLevelId
    createdDate
    {| Approval = "approved"; CreatedDate = createdDate |}
    None
    (CommentWriter.Commenter (commenter CommenterStatus.Trusted))
    None
    "A question"
  |> ok

[<RequireQualifiedAccess>]
type private Write =
  | Stop
  | Comment
  | Notice

let private tableDown = { Dependency = Dependency.Database; Type = Unknown (exn "The table is down") }

type private FakePostComment(existing : Commenter, failing : Write option) =
  let stored = ResizeArray<ReplyEmailConsent option>()
  let stops = ResizeArray<ReplyEmailStop>()
  let writes = ResizeArray<Write>()

  let write (kind : Write) (value : 'a) =
    if failing = Some kind then
      CancellableTaskResult.error tableDown
    else
      writes.Add kind
      CancellableTaskResult.singleton value

  new(existing : Commenter) = FakePostComment(existing, None)

  member _.Stored = List.ofSeq stored
  member _.Stops = List.ofSeq stops
  member _.Writes = List.ofSeq writes

  interface PostComment.Dependencies with
    member _.GetPostBySlug _ = CancellableTaskResult.singleton (Some olderPost)
    member _.GetUtcNow () = now
    member _.GetPostedComment _ _ =
      CancellableTaskResult.singleton (Some { Post = olderPost.Id; PostTitle = olderPost.Info.Title; Comment = topLevel })
    member _.GetCommenterByValidationId _ = CancellableTaskResult.singleton (Some existing)
    member _.NewCommentId () = CommentId.ofGuid (Guid newId)
    member _.CreateComment id _ commenter newComment topLevel approval replyEmails =
      let createdDate = now.ToString "o"

      let created =
        Comment.create
          (string id)
          createdDate
          {| Approval = approval.ToString(); CreatedDate = createdDate |}
          (topLevel |> Option.map (_.Id >> string))
          (CommentWriter.Commenter commenter)
          replyEmails
          (newComment.Content.ToString())
        |> ok

      write Write.Comment created |> CancellableTaskResult.map (fun created -> stored.Add replyEmails; created)
    member _.NewReplyEmailStopToken () = ReplyEmailStopToken.ofGuid token
    member _.CreateReplyEmailStop stop =
      write Write.Stop () |> CancellableTaskResult.map (fun () -> stops.Add stop)
    member _.QueueNewCommentNotice _ _ = write Write.Notice ()
    member _.QueueReplyEmail _ _ = CancellableTaskResult.singleton ()
    member _.Stage = TwoPoint.Site.Render.Stage.Production

let private post (replyTo : string option) (replyEmails : bool) (dependencies : FakePostComment) =
  let dto =
    { NewComment.Dto.Post = NewComment.Post (olderPost.Id.ToString())
      NewComment.Dto.ValidationId = NewComment.ValidationId commenterId
      NewComment.Dto.Comment = NewComment.Comment "A comment"
      NewComment.Dto.ReplyTo = replyTo |> Option.map NewComment.ReplyTo
      NewComment.Dto.ReplyEmails = NewComment.ReplyEmails replyEmails }

  (PostComment.withDependencies dependencies dto CancellationToken.None).Result

[<Tests>]
let tests =
  testList "the reply-email consent" [
    test "a checked flag on a top-level comment stores the consent with its date and token, and the stop row" {
      let dependencies = FakePostComment(commenter CommenterStatus.Trusted)
      let consent = { ConsentDate = now; StopToken = ReplyEmailStopToken.ofGuid token; StopDate = None }

      match post None true dependencies with
      | Ok (PostComment.Event.Posted (_, posted)) ->
        Expect.equal dependencies.Stored [ Some consent ] "Expected the consent in the store"
        Expect.equal posted.Comment.ReplyEmails (Some consent) "Expected the consent on the comment"

        Expect.equal
          dependencies.Stops
          [ { Token = ReplyEmailStopToken.ofGuid token; Post = olderPost.Id; Comment = posted.Comment.Id } ]
          "Expected the stop row of the token"
      | other -> failtestf "Expected Posted, got %A" other
    }

    test "a checked flag from a new commenter stores the consent too" {
      let dependencies = FakePostComment(commenter CommenterStatus.New)

      match post None true dependencies with
      | Ok (PostComment.Event.Posted (_, posted)) ->
        Expect.equal posted.Comment.Status.Approval CommentApproval.New "Expected a comment that waits for approval"
        Expect.isSome posted.Comment.ReplyEmails "Expected the consent"
        Expect.equal dependencies.Stops.Length 1 "Expected the stop row"
      | other -> failtestf "Expected Posted, got %A" other
    }

    test "an unchecked flag stores no consent and no stop row" {
      let dependencies = FakePostComment(commenter CommenterStatus.Trusted)

      match post None false dependencies with
      | Ok (PostComment.Event.Posted (_, posted)) ->
        Expect.equal dependencies.Stored [ None ] "Expected no consent in the store"
        Expect.isNone posted.Comment.ReplyEmails "Expected no consent on the comment"
        Expect.isEmpty dependencies.Stops "Expected no stop row"
      | other -> failtestf "Expected Posted, got %A" other
    }

    test "a reply stores no consent and no stop row" {
      let dependencies = FakePostComment(commenter CommenterStatus.Trusted)

      match post (Some topLevelId) false dependencies with
      | Ok (PostComment.Event.Posted (_, posted)) ->
        Expect.equal dependencies.Stored [ None ] "Expected no consent in the store"
        Expect.isNone posted.Comment.ReplyEmails "Expected no consent on the reply"
        Expect.isEmpty dependencies.Stops "Expected no stop row"
      | other -> failtestf "Expected Posted, got %A" other
    }

    test "replyTo with a checked flag is refused as invalid, and the blog stores nothing" {
      let dependencies = FakePostComment(commenter CommenterStatus.Trusted)

      match post (Some topLevelId) true dependencies with
      | Error (ActionError.Validation errors) ->
        Expect.equal errors [ NewComment.replyEmailsOnReplyMessage ] "Expected the reply-email message"
        Expect.isEmpty dependencies.Stored "Expected no comment in the store"
        Expect.isEmpty dependencies.Stops "Expected no stop row"
      | other -> failtestf "Expected a validation error, got %A" other
    }

    test "a banned commenter with a checked flag gets no comment and no stop row" {
      let dependencies = FakePostComment(commenter CommenterStatus.Banned)

      match post None true dependencies with
      | Error (Logic (PostComment.Error.Banned _)) ->
        Expect.isEmpty dependencies.Stored "Expected no comment in the store"
        Expect.isEmpty dependencies.Stops "Expected no stop row"
      | other -> failtestf "Expected Banned, got %A" other
    }

    test "a checked flag writes the stop row before the comment, and the comment before the notice" {
      let dependencies = FakePostComment(commenter CommenterStatus.Trusted)

      match post None true dependencies with
      | Ok (PostComment.Event.Posted (_, posted)) ->
        Expect.equal dependencies.Writes [ Write.Stop; Write.Comment; Write.Notice ] "Expected the stop row first"
        Expect.equal dependencies.Stops.Head.Comment posted.Comment.Id "Expected the stop row of the new comment"
      | other -> failtestf "Expected Posted, got %A" other
    }

    test "a failed stop-row write stores no comment and queues no notice" {
      let dependencies = FakePostComment(commenter CommenterStatus.Trusted, Some Write.Stop)

      match post None true dependencies with
      | Error (ActionError.Dependency error) ->
        Expect.equal error tableDown "Expected the error of the stop-row write"
        Expect.isEmpty dependencies.Writes "Expected no write"
      | other -> failtestf "Expected a dependency error, got %A" other
    }

    test "a failed comment write queues no notice, and only the stop row stays" {
      let dependencies = FakePostComment(commenter CommenterStatus.Trusted, Some Write.Comment)

      match post None true dependencies with
      | Error (ActionError.Dependency error) ->
        Expect.equal error tableDown "Expected the error of the comment write"
        Expect.equal dependencies.Writes [ Write.Stop ] "Expected only the stop row"
      | other -> failtestf "Expected a dependency error, got %A" other
    }
  ]
