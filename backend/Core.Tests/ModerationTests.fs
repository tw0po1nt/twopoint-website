module TwoPoint.Core.Tests.ModerationTests

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

let private comment (id : string) (createdDate : string) (approval : CommentApproval) =
  Comment.create
    id
    createdDate
    {| Approval = approval.ToString(); CreatedDate = createdDate |}
    {| ValidationId = "7d1f0c55-3c1b-4a43-9a0e-2b5f6f0d8e21"
       EmailAddress = "commenter@example.com"
       Name = Some "Commenter"
       CreatedDate = "2026-01-01T00:00:00.0000000Z"
       Status = "new" |}
    "A comment"
  |> ok

let private firstId = "0b6c1f0e-2f0a-4c8e-9a51-6d7c2b1e3f40"

type private FakeApprovalDependencies(existing : Comment option) =
  let updates = ResizeArray<CommentApproval>()

  member _.Updates = List.ofSeq updates

  interface UpdateCommentApproval.Dependencies with
    member _.GetCommentById _ = CancellableTaskResult.singleton existing
    member _.UpdateCommentApproval comment approval =
      updates.Add approval
      CancellableTaskResult.singleton { comment with Status = { comment.Status with Approval = approval } }

let private update (dependencies : FakeApprovalDependencies) (approval : CommentApproval) =
  let dto =
    { CommentApprovalUpdate.Dto.CommentId = CommentApprovalUpdate.CommentId firstId
      CommentApprovalUpdate.Dto.Approval = CommentApprovalUpdate.Approval (approval.ToString()) }
  (UpdateCommentApproval.withDependencies dependencies dto CancellationToken.None).Result

let private approvalTests =
  testList "UpdateCommentApproval" [
    test "an approval leaves the commenter status as it was" {
      let existing = comment firstId "2026-02-01T00:00:00.0000000Z" CommentApproval.New
      let dependencies = FakeApprovalDependencies (Some existing)

      match update dependencies CommentApproval.Approved with
      | Ok (UpdateCommentApproval.Event.Updated updated) ->
        Expect.equal updated.Status.Approval CommentApproval.Approved "Expected an approved comment"
        Expect.equal updated.Commenter existing.Commenter "Expected the same commenter"
        Expect.equal updated.Commenter.Status CommenterStatus.New "Expected a new commenter"
      | other -> failtestf "Expected Updated, got %A" other
    }

    testList "allowed changes" [
      for current, requested in
        [ CommentApproval.New, CommentApproval.Approved
          CommentApproval.New, CommentApproval.Rejected
          CommentApproval.Approved, CommentApproval.Rejected
          CommentApproval.Rejected, CommentApproval.Approved ] do
        test $"{current} to {requested}" {
          let dependencies = FakeApprovalDependencies (Some (comment firstId "2026-02-01T00:00:00.0000000Z" current))

          match update dependencies requested with
          | Ok (UpdateCommentApproval.Event.Updated updated) ->
            Expect.equal updated.Status.Approval requested "Expected the requested approval"
            Expect.equal dependencies.Updates [ requested ] "Expected one update"
          | other -> failtestf "Expected Updated, got %A" other
        }
    ]

    testList "changes that are not allowed" [
      for current, requested in
        [ CommentApproval.New, CommentApproval.New
          CommentApproval.Approved, CommentApproval.New
          CommentApproval.Rejected, CommentApproval.New
          CommentApproval.Approved, CommentApproval.Approved
          CommentApproval.Rejected, CommentApproval.Rejected ] do
        test $"{current} to {requested}" {
          let dependencies = FakeApprovalDependencies (Some (comment firstId "2026-02-01T00:00:00.0000000Z" current))

          Expect.equal
            (update dependencies requested)
            (Error (Logic (UpdateCommentApproval.ChangeNotAllowed (current, requested))))
            "Expected ChangeNotAllowed"
          Expect.isEmpty dependencies.Updates "Expected no update"
        }
    ]

    test "a missing comment gets no update" {
      let dependencies = FakeApprovalDependencies None

      match update dependencies CommentApproval.Approved with
      | Error (Logic (UpdateCommentApproval.CommentNotFound _)) ->
        Expect.isEmpty dependencies.Updates "Expected no update"
      | other -> failtestf "Expected CommentNotFound, got %A" other
    }
  ]

type private FakeQueueDependencies(comments : QueuedComment list) =
  let requests = ResizeArray<CommentApproval list>()

  member _.Requests = List.ofSeq requests

  interface GetModerationQueue.Dependencies with
    member _.GetCommentsOnAllPosts approvals =
      requests.Add approvals
      CancellableTaskResult.singleton comments

let private queued (post : Post) (createdDate : string) =
  { Post = post.Id
    PostTitle = post.Info.Title
    Comment = comment (Guid.NewGuid().ToString()) createdDate CommentApproval.New }

let private queueTests =
  testList "GetModerationQueue" [
    test "the queue has the new comments on all posts, oldest first" {
      let newest = queued olderPost "2026-02-03T00:00:00.0000000Z"
      let oldest = queued latestPost "2026-02-01T00:00:00.0000000Z"
      let middle = queued olderPost "2026-02-02T00:00:00.0000000Z"
      let dependencies = FakeQueueDependencies [ newest; oldest; middle ]

      Expect.equal
        (GetModerationQueue.withDependencies dependencies () CancellationToken.None).Result
        (Ok [ oldest; middle; newest ])
        "Expected the oldest comment first"
      Expect.equal dependencies.Requests [ [ CommentApproval.New ] ] "Expected only the new comments"
    }
  ]

type private FakePostCommentsDependencies(post : Post, comments : Comment list) =
  interface GetCommentsForPost.Dependencies with
    member _.GetPostBySlug _ = CancellableTaskResult.singleton (Some post)
    member _.GetCommentsForPost _ _ = CancellableTaskResult.singleton comments

let private postCommentsTests =
  testList "GetCommentsForPost" [
    test "the comments of a post come oldest first" {
      let newest = comment (Guid.NewGuid().ToString()) "2026-02-03T00:00:00.0000000Z" CommentApproval.Rejected
      let oldest = comment (Guid.NewGuid().ToString()) "2026-02-01T00:00:00.0000000Z" CommentApproval.Approved
      let middle = comment (Guid.NewGuid().ToString()) "2026-02-02T00:00:00.0000000Z" CommentApproval.New
      let dependencies = FakePostCommentsDependencies (olderPost, [ newest; oldest; middle ])

      Expect.equal
        (GetCommentsForPost.withDependencies dependencies [] (olderPost.Id.ToString()) CancellationToken.None).Result
        (Ok (Some [ oldest; middle; newest ]))
        "Expected the oldest comment first"
    }
  ]

[<Tests>]
let tests = testList "Moderation" [ approvalTests; queueTests; postCommentsTests ]
