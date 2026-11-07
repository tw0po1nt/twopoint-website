module TwoPoint.Core.Tests.NewCommentTests

open TwoPoint.Core.Notifications
open TwoPoint.Core.Notifications.Actions
open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open Expecto
open FsToolkit.ErrorHandling

open System
open System.Text.Json
open System.Threading

let private commentId = "0b6c1f0e-2f0a-4c8e-9a51-6d7c2b1e3f40"
let private authorId = "6f1c1b5e-3d2a-4a43-9c6b-0d7a5b0e9a11"

let private posted (approval : CommentApproval) (title : string) (name : string) (content : string) =
  let createdDate = "2026-02-01T00:00:00.0000000Z"
  let comment =
    Comment.create
      commentId
      createdDate
      {| Approval = approval.ToString(); CreatedDate = createdDate |}
      None
      (Commenter.create
        "7d1f0c55-3c1b-4a43-9a0e-2b5f6f0d8e21"
        "commenter@example.com"
        (Some name)
        "2026-01-01T00:00:00.0000000Z"
        "new"
       |> Fixtures.ok
       |> CommentWriter.Commenter)
      content
    |> Fixtures.ok

  { PostedComment.Post = Slug.Unsafe.create "a-post"
    PostTitle = NonEmptyString.create None title |> Fixtures.ok
    Comment = comment }

let private device (endpoint : string) =
  let at = DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc)
  let endpoint = PushEndpoint.create endpoint |> Fixtures.ok
  { RegisteredDevice.UserId = UserExternalId.create authorId |> Fixtures.ok
    Id = DeviceId.ofEndpoint endpoint
    Endpoint = endpoint
    P256dh = NonEmptyString.create None "BNcRdre" |> Fixtures.ok
    Auth = NonEmptyString.create None "tBHItJI" |> Fixtures.ok
    Label = NonEmptyString.create None "Chrome on Windows" |> Fixtures.ok
    CreatedDate = at
    LastSeenDate = at }

let private first = device "https://push.example.com/send/one"
let private second = device "https://push.example.com/send/two"
let private third = device "https://push.example.com/send/three"

let private dependencyError : DependencyError =
  { Dependency = Dependency.WebPush; Type = Unknown (exn "The push service answered 500") }

type private Fake(comment : PostedComment option, devices : RegisteredDevice list, answers : Map<DeviceId, Result<PushOutcome, DependencyError>>) =
  let sent = ResizeArray<RegisteredDevice * byte array>()
  let recorded = ResizeArray<RegisteredDevice>()
  let deleted = ResizeArray<RegisteredDevice>()

  member _.Sent = List.ofSeq sent
  member _.Recorded = List.ofSeq recorded
  member _.Deleted = List.ofSeq deleted

  interface NotifyNewComment.Dependencies with
    member _.GetPostedComment _ = CancellableTaskResult.singleton comment
    member _.CountNewComments () = CancellableTaskResult.singleton 3
    member _.GetDevicesOfAuthor () = CancellableTaskResult.singleton devices
    member _.SendPush device payload =
      sent.Add(device, payload)

      match answers |> Map.tryFind device.Id with
      | Some (Ok outcome) -> CancellableTaskResult.singleton outcome
      | Some (Error error) -> CancellableTaskResult.error error
      | None -> CancellableTaskResult.singleton Delivered
    member _.RecordPush device _ =
      recorded.Add device
      CancellableTaskResult.singleton ()
    member _.DeleteDevice device =
      deleted.Add device
      CancellableTaskResult.singleton ()

let private notify (fake : Fake) =
  let dto : NotifyNewComment.Dto = { CommentId = commentId }
  (NotifyNewComment.withDependencies fake dto CancellationToken.None).Result

let private waiting = posted CommentApproval.New "A post" "Ada" "The text."

let private field (json : JsonDocument) (name : string) = json.RootElement.GetProperty name

let private handlerTests =
  testList "NotifyNewComment" [
    test "it sends to each device and records each send" {
      let fake = Fake(Some waiting, [ first; second ], Map.empty)

      match notify fake with
      | Ok (NotifyNewComment.Event.Notified result) ->
        Expect.equal result.Sent [ first; second ] "Expected a send to each device"
        Expect.equal fake.Recorded [ first; second ] "Expected a push row for each send"
        Expect.isEmpty result.Removed "Expected no removed device"
        Expect.isEmpty result.Failed "Expected no failure"
      | other -> failtestf "Expected Notified, got %A" other
    }

    test "it deletes a device that the push service reports as gone" {
      let fake = Fake(Some waiting, [ first; second ], Map [ first.Id, Ok DeviceGone ])

      match notify fake with
      | Ok (NotifyNewComment.Event.Notified result) ->
        Expect.equal fake.Deleted [ first ] "Expected the first device to go"
        Expect.equal result.Removed [ first ] "Expected the first device in the removed list"
        Expect.equal result.Sent [ second ] "Expected a send to the second device"
        Expect.equal fake.Recorded [ second ] "Expected no push row for a gone device"
      | other -> failtestf "Expected Notified, got %A" other
    }

    test "a failed push goes in the failed list, and the next device still gets its push" {
      let fake = Fake(Some waiting, [ first; second; third ], Map [ second.Id, Error dependencyError ])

      match notify fake with
      | Ok (NotifyNewComment.Event.Notified result) ->
        Expect.equal result.Sent [ first; third ] "Expected a send to the other devices"
        Expect.equal (result.Failed |> List.map fst) [ second ] "Expected the second device in the failed list"
        Expect.isEmpty fake.Deleted "A failure does not delete a device"
      | other -> failtestf "Expected Notified, got %A" other
    }

    test "the payload has the case, the comment, the post, and the count of new comments" {
      let fake = Fake(Some waiting, [ first ], Map.empty)
      notify fake |> ignore
      use json = JsonDocument.Parse(snd fake.Sent.Head)

      Expect.equal ((field json "case").GetString()) "awaits-moderation" "Expected the case"
      Expect.equal ((field json "commentId").GetString()) commentId "Expected the comment ID"
      Expect.equal ((field json "slug").GetString()) "a-post" "Expected the slug"
      Expect.equal ((field json "postTitle").GetString()) "A post" "Expected the post title"
      Expect.equal ((field json "commenter").GetString()) "Ada" "Expected the commenter name"
      Expect.equal ((field json "text").GetString()) "The text." "Expected the text"
      Expect.equal ((field json "newCount").GetInt32()) 3 "Expected the count"
    }

    test "the comment of a trusted commenter has the case approved" {
      let fake = Fake(Some (posted CommentApproval.Approved "A post" "Ada" "Hi"), [ first ], Map.empty)
      notify fake |> ignore
      use json = JsonDocument.Parse(snd fake.Sent.Head)

      Expect.equal ((field json "case").GetString()) "approved" "Expected the case"
    }

    test "a rejected comment sends nothing" {
      let fake = Fake(Some (posted CommentApproval.Rejected "A post" "Ada" "Hi"), [ first ], Map.empty)

      match notify fake with
      | Ok (NotifyNewComment.Event.Skipped _) -> Expect.isEmpty fake.Sent "Expected no push"
      | other -> failtestf "Expected Skipped, got %A" other
    }

    test "a comment that is gone sends nothing" {
      let fake = Fake(None, [ first ], Map.empty)

      match notify fake with
      | Ok (NotifyNewComment.Event.Skipped _) -> Expect.isEmpty fake.Sent "Expected no push"
      | other -> failtestf "Expected Skipped, got %A" other
    }
  ]

let private payloadTests =
  testList "NewCommentPayload" [
    test "the largest payload stays below 4 KB" {
      // A control character takes 6 octets in JSON, so it is the worst case for the limits.
      let control = String('\u0001', 5000)
      let astral = String.replicate 5000 "\U0001F600"

      for text in [ control; astral ] do
        let comment = posted CommentApproval.New text text text

        match NewCommentPayload.ofComment 999999 comment with
        | Some payload ->
          let bytes = NewCommentPayload.toBytes payload
          Expect.isLessThan bytes.Length 4096 "Expected a payload below 4 KB"
          Expect.isLessThanOrEqual bytes.Length NewCommentPayload.maxOctets "Expected a payload that the sender takes"
        | None -> failtest "Expected a payload"
    }

    test "the text is the first 120 characters" {
      let comment = posted CommentApproval.New "A post" "Ada" (String('x', 300))

      match NewCommentPayload.ofComment 1 comment with
      | Some payload -> Expect.equal payload.Text.Length 120 "Expected 120 characters"
      | None -> failtest "Expected a payload"
    }

    test "the text is the plain text of the comment, with no marks and no line breaks" {
      let source = "A **great** post.\n\nSee [the docs](https://learn.microsoft.com/) and `List.map`:\n\n- one\n- two"
      let comment = posted CommentApproval.New "A post" "Ada" source

      match NewCommentPayload.ofComment 1 comment with
      | Some payload -> Expect.equal payload.Text "A great post. See the docs and List.map: one two" "Expected the plain text"
      | None -> failtest "Expected a payload"
    }

    test "the text is the first 120 characters of the plain text" {
      let source = String.replicate 100 "**ab** "
      let comment = posted CommentApproval.New "A post" "Ada" source
      let expected = (String.replicate 100 "ab ").Substring(0, 120)

      match NewCommentPayload.ofComment 1 comment with
      | Some payload -> Expect.equal payload.Text expected "Expected 120 characters of the plain text"
      | None -> failtest "Expected a payload"
    }

    test "a comment of the author gets no payload" {
      let posted = posted CommentApproval.Approved "A post" "Ada" "Hi"
      let comment = { posted with Comment = { posted.Comment with Writer = CommentWriter.Author } }

      Expect.isNone (NewCommentPayload.ofComment 1 comment) "Expected no payload"
    }

    test "a comment with no plain text gets the no-text mark" {
      let comment = posted CommentApproval.New "A post" "Ada" "![](https://x.com/a.png)"

      match NewCommentPayload.ofComment 1 comment with
      | Some payload -> Expect.equal payload.Text "(no text)" "Expected the no-text mark"
      | None -> failtest "Expected a payload"
    }
  ]

[<Tests>]
let tests = testList "NewComment" [ handlerTests; payloadTests ]
