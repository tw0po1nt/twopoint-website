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

let private commenterId = "7d1f0c55-3c1b-4a43-9a0e-2b5f6f0d8e21"

let private commenter (status : CommenterStatus) =
  Commenter.create commenterId "commenter@example.com" (Some "Commenter") "2026-01-01T00:00:00.0000000Z" (status.ToString())
  |> ok

type private FakePostCommentDependencies(existing : Commenter) =
  let created = ResizeArray<CommentApproval>()

  member _.Created = List.ofSeq created

  interface PostComment.Dependencies with
    member _.GetPostBySlug _ = CancellableTaskResult.singleton (Some olderPost)
    member _.GetCommenterByValidationId _ = CancellableTaskResult.singleton (Some existing)
    member _.CreateComment _ commenter newComment approval =
      created.Add approval
      let posted = comment firstId "2026-02-01T00:00:00.0000000Z" approval
      CancellableTaskResult.singleton { posted with Commenter = commenter; Content = newComment.Content }

let private postComment (dependencies : FakePostCommentDependencies) =
  let dto =
    { NewComment.Dto.Post = NewComment.Post (olderPost.Id.ToString())
      NewComment.Dto.ValidationId = NewComment.ValidationId commenterId
      NewComment.Dto.Comment = NewComment.Comment "A comment" }
  (PostComment.withDependencies dependencies dto CancellationToken.None).Result

let private postCommentTests =
  testList "PostComment" [
    test "a comment of a trusted commenter is approved at the time of the post" {
      let dependencies = FakePostCommentDependencies (commenter CommenterStatus.Trusted)

      match postComment dependencies with
      | Ok (PostComment.Event.Posted (_, posted)) ->
        Expect.equal posted.Status.Approval CommentApproval.Approved "Expected an approved comment"
        Expect.equal dependencies.Created [ CommentApproval.Approved ] "Expected one approved comment in the store"
      | other -> failtestf "Expected Posted, got %A" other
    }

    test "a comment of a new commenter is new" {
      let dependencies = FakePostCommentDependencies (commenter CommenterStatus.New)

      match postComment dependencies with
      | Ok (PostComment.Event.Posted (_, posted)) ->
        Expect.equal posted.Status.Approval CommentApproval.New "Expected a new comment"
        Expect.equal dependencies.Created [ CommentApproval.New ] "Expected one new comment in the store"
      | other -> failtestf "Expected Posted, got %A" other
    }

    test "a banned commenter posts, and the blog stores nothing and notifies no one" {
      let dependencies = FakePostCommentDependencies (commenter CommenterStatus.Banned)

      match postComment dependencies with
      | Error (Logic (PostComment.Banned _)) ->
        Expect.isEmpty dependencies.Created "Expected no comment in the store, so no notification"
      | other -> failtestf "Expected Banned, got %A" other
    }
  ]

type private FakeValidationDependencies(existing : Commenter option) =
  let created = ResizeArray<EmailAddress>()
  let sent = ResizeArray<Commenter>()

  member _.Created = List.ofSeq created
  member _.Sent = List.ofSeq sent

  interface ValidateCommenter.Dependencies with
    member _.GetCommenterByEmailAddress _ = CancellableTaskResult.singleton existing
    member _.GetValidRedirectUris () = CancellableTaskResult.singleton [ Uri "https://twopoint.dev" ]
    member _.CreateCommenter emailAddress _ _ =
      created.Add emailAddress
      CancellableTaskResult.singleton (commenter CommenterStatus.New)
    member _.SendCommenterValidationEmail commenter _ =
      sent.Add commenter
      CancellableTaskResult.singleton "operation"

let private validateCommenter (dependencies : FakeValidationDependencies) =
  let dto =
    { CommenterValidation.Dto.EmailAddress = CommenterValidation.EmailAddress "commenter@example.com"
      CommenterValidation.Dto.Name = Some (CommenterValidation.Name "Commenter")
      CommenterValidation.Dto.RedirectUri = CommenterValidation.RedirectUri "https://twopoint.dev/posts/older-post" }
  (ValidateCommenter.withDependencies dependencies dto CancellationToken.None).Result

let private validationTests =
  testList "ValidateCommenter" [
    test "a banned commenter verifies again, and the blog sends no verification email" {
      let dependencies = FakeValidationDependencies (Some (commenter CommenterStatus.Banned))

      match validateCommenter dependencies with
      | Error (Logic (ValidateCommenter.Banned _)) ->
        Expect.isEmpty dependencies.Sent "Expected no verification email"
        Expect.isEmpty dependencies.Created "Expected no new commenter"
      | other -> failtestf "Expected Banned, got %A" other
    }

    test "a new commenter gets a verification email" {
      let dependencies = FakeValidationDependencies (Some (commenter CommenterStatus.New))

      match validateCommenter dependencies with
      | Ok (ValidateCommenter.Event.Validated _) ->
        Expect.equal dependencies.Sent [ commenter CommenterStatus.New ] "Expected one verification email"
      | other -> failtestf "Expected Validated, got %A" other
    }
  ]

type private FakeStatusDependencies(existing : Commenter option, comments : Comment list) =
  let updates = ResizeArray<CommenterStatus>()
  let approvals = ResizeArray<CommentId * CommentApproval>()

  member _.Updates = List.ofSeq updates
  member _.Approvals = List.ofSeq approvals

  interface UpdateCommenterStatus.Dependencies with
    member _.GetCommenterByValidationId _ = CancellableTaskResult.singleton existing
    member _.GetCommentsOfCommenter _ = CancellableTaskResult.singleton comments
    member _.UpdateCommenterStatus commenter status =
      updates.Add status
      CancellableTaskResult.singleton { commenter with Status = status }
    member _.UpdateCommentApproval comment approval =
      approvals.Add (comment.Id, approval)
      CancellableTaskResult.singleton { comment with Status = { comment.Status with Approval = approval } }

let private newComment = comment "1c6c1f0e-2f0a-4c8e-9a51-6d7c2b1e3f41" "2026-02-01T00:00:00.0000000Z" CommentApproval.New
let private approvedComment = comment "2c6c1f0e-2f0a-4c8e-9a51-6d7c2b1e3f42" "2026-02-02T00:00:00.0000000Z" CommentApproval.Approved
let private rejectedComment = comment "3c6c1f0e-2f0a-4c8e-9a51-6d7c2b1e3f43" "2026-02-03T00:00:00.0000000Z" CommentApproval.Rejected
let private commentsOfCommenter = [ newComment; approvedComment; rejectedComment ]

let private updateStatus (dependencies : FakeStatusDependencies) (status : CommenterStatus) =
  let dto =
    { CommenterStatusUpdate.Dto.ValidationId = CommenterStatusUpdate.ValidationId commenterId
      CommenterStatusUpdate.Dto.Status = CommenterStatusUpdate.Status (status.ToString()) }
  (UpdateCommenterStatus.withDependencies dependencies dto CancellationToken.None).Result

let private statusTests =
  testList "UpdateCommenterStatus" [
    test "trust makes a new commenter trusted, and leaves each comment as it was" {
      let dependencies = FakeStatusDependencies (Some (commenter CommenterStatus.New), commentsOfCommenter)

      match updateStatus dependencies CommenterStatus.Trusted with
      | Ok (UpdateCommenterStatus.Event.Updated updated) ->
        Expect.equal updated.Status CommenterStatus.Trusted "Expected a trusted commenter"
        Expect.equal dependencies.Updates [ CommenterStatus.Trusted ] "Expected one update"
        Expect.isEmpty dependencies.Approvals "Expected no comment change"
      | other -> failtestf "Expected Updated, got %A" other
    }

    test "a change from trusted to new makes the commenter new, and leaves each comment as it was" {
      let dependencies = FakeStatusDependencies (Some (commenter CommenterStatus.Trusted), commentsOfCommenter)

      match updateStatus dependencies CommenterStatus.New with
      | Ok (UpdateCommenterStatus.Event.Updated updated) ->
        Expect.equal updated.Status CommenterStatus.New "Expected a new commenter"
        Expect.equal dependencies.Updates [ CommenterStatus.New ] "Expected the status as the only change"
        Expect.isEmpty dependencies.Approvals "Expected no comment change"
      | other -> failtestf "Expected Updated, got %A" other
    }

    testList "a ban rejects each new comment and keeps each approved comment" [
      for current in [ CommenterStatus.New; CommenterStatus.Trusted ] do
        test $"from {current}" {
          let dependencies = FakeStatusDependencies (Some (commenter current), commentsOfCommenter)

          match updateStatus dependencies CommenterStatus.Banned with
          | Ok (UpdateCommenterStatus.Event.Updated updated) ->
            Expect.equal updated.Status CommenterStatus.Banned "Expected a banned commenter"
            Expect.equal dependencies.Updates [ CommenterStatus.Banned ] "Expected one update"
            Expect.equal
              dependencies.Approvals
              [ newComment.Id, CommentApproval.Rejected ]
              "Expected only the new comment to become rejected"
          | other -> failtestf "Expected Updated, got %A" other
        }
    ]

    test "an unban makes the commenter new, and leaves each rejected comment rejected" {
      let dependencies = FakeStatusDependencies (Some (commenter CommenterStatus.Banned), [ approvedComment; rejectedComment ])

      match updateStatus dependencies CommenterStatus.New with
      | Ok (UpdateCommenterStatus.Event.Updated updated) ->
        Expect.equal updated.Status CommenterStatus.New "Expected a new commenter"
        Expect.equal dependencies.Updates [ CommenterStatus.New ] "Expected the status as the only change"
        Expect.isEmpty dependencies.Approvals "Expected no comment change"
      | other -> failtestf "Expected Updated, got %A" other
    }

    test "a second change keeps the status" {
      for status in [ CommenterStatus.New; CommenterStatus.Trusted; CommenterStatus.Banned ] do
        let dependencies = FakeStatusDependencies (Some (commenter status), [])

        match updateStatus dependencies status with
        | Ok (UpdateCommenterStatus.Event.Updated updated) ->
          Expect.equal updated.Status status $"Expected a {status} commenter"
        | other -> failtestf "Expected Updated, got %A" other
    }

    test "a second ban rejects each new comment that the first ban left" {
      let dependencies = FakeStatusDependencies (Some (commenter CommenterStatus.Banned), commentsOfCommenter)

      match updateStatus dependencies CommenterStatus.Banned with
      | Ok (UpdateCommenterStatus.Event.Updated _) ->
        Expect.equal dependencies.Approvals [ newComment.Id, CommentApproval.Rejected ] "Expected the new comment rejected"
      | other -> failtestf "Expected Updated, got %A" other
    }

    test "a banned commenter cannot become trusted" {
      let dependencies = FakeStatusDependencies (Some (commenter CommenterStatus.Banned), commentsOfCommenter)

      Expect.equal
        (updateStatus dependencies CommenterStatus.Trusted)
        (Error (Logic (UpdateCommenterStatus.ChangeNotAllowed (CommenterStatus.Banned, CommenterStatus.Trusted))))
        "Expected ChangeNotAllowed"
      Expect.isEmpty dependencies.Updates "Expected no update"
      Expect.isEmpty dependencies.Approvals "Expected no comment change"
    }

    test "a missing commenter gets no update" {
      let dependencies = FakeStatusDependencies (None, [])

      match updateStatus dependencies CommenterStatus.Trusted with
      | Error (Logic (UpdateCommenterStatus.CommenterNotFound _)) ->
        Expect.isEmpty dependencies.Updates "Expected no update"
      | other -> failtestf "Expected CommenterNotFound, got %A" other
    }
  ]

[<Tests>]
let tests = testList "Moderation" [ approvalTests; queueTests; postCommentsTests; postCommentTests; validationTests; statusTests ]
