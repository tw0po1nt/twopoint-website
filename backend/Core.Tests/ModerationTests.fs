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

let private commentWith (content : string) (id : string) (createdDate : string) (approval : CommentApproval) =
  Comment.create
    id
    createdDate
    {| Approval = approval.ToString(); CreatedDate = createdDate |}
    None
    (Commenter.create
      "7d1f0c55-3c1b-4a43-9a0e-2b5f6f0d8e21"
      "commenter@example.com"
      (Some "Commenter")
      "2026-01-01T00:00:00.0000000Z"
      "new"
     |> ok
     |> CommentWriter.Commenter)
    content
  |> ok

let private comment = commentWith "A comment"

let private markdown = "Use `List.map` and see https://example.com/."

let private markdownHtml =
  "<p>Use <code>List.map</code> and see <a href=\"https://example.com/\" target=\"_blank\" rel=\"nofollow ugc noopener noreferrer\">https://example.com/</a>.</p>\n"

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
        Expect.equal updated.Writer existing.Writer "Expected the same commenter"
        Expect.equal (Comment.commenter updated |> Option.map _.Status) (Some CommenterStatus.New) "Expected a new commenter"
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

type private FakeQueueDependencies(comments : PostedComment list) =
  let requests = ResizeArray<CommentApproval list>()

  member _.Requests = List.ofSeq requests

  interface GetModerationQueue.Dependencies with
    member _.GetCommentsOnAllPosts approvals =
      requests.Add approvals
      CancellableTaskResult.singleton comments
    member _.Stage = TwoPoint.Site.Render.Stage.Production

let private queuedWith (content : string) (post : Post) (createdDate : string) =
  { PostedComment.Post = post.Id
    PostTitle = post.Info.Title
    Comment = commentWith content (Guid.NewGuid().ToString()) createdDate CommentApproval.New }

let private queued = queuedWith "A comment"

let private rendered (posted : PostedComment) =
  RenderedPostedComment.ofPostedComment TwoPoint.Site.Render.Stage.Production posted

let private queueTests =
  testList "GetModerationQueue" [
    test "the queue has the new comments on all posts, oldest first" {
      let newest = queued olderPost "2026-02-03T00:00:00.0000000Z"
      let oldest = queued latestPost "2026-02-01T00:00:00.0000000Z"
      let middle = queued olderPost "2026-02-02T00:00:00.0000000Z"
      let dependencies = FakeQueueDependencies [ newest; oldest; middle ]

      Expect.equal
        (GetModerationQueue.withDependencies dependencies () CancellationToken.None).Result
        (Ok ([ oldest; middle; newest ] |> List.map rendered))
        "Expected the oldest comment first"
      Expect.equal dependencies.Requests [ [ CommentApproval.New ] ] "Expected only the new comments"
    }

    test "the queue gives the rendered HTML" {
      let posted = queuedWith markdown olderPost "2026-02-01T00:00:00.0000000Z"
      let dependencies = FakeQueueDependencies [ posted ]

      match (GetModerationQueue.withDependencies dependencies () CancellationToken.None).Result with
      | Ok [ queued ] ->
        Expect.equal queued.Comment.Html markdownHtml "Expected the HTML of the source"
        Expect.equal queued.Comment.Comment posted.Comment "Expected the stored comment"
      | other -> failtestf "Expected one comment, got %A" other
    }
  ]

type private FakePostCommentsDependencies(post : Post, comments : Comment list) =
  interface GetCommentsForPost.Dependencies with
    member _.GetPostBySlug _ = CancellableTaskResult.singleton (Some post)
    member _.GetCommentsForPost _ _ = CancellableTaskResult.singleton comments
    member _.Stage = TwoPoint.Site.Render.Stage.Production

let private getCommentsForPost (dependencies : FakePostCommentsDependencies) =
  (GetCommentsForPost.withDependencies dependencies [] (olderPost.Id.ToString()) CancellationToken.None).Result

let private postCommentsTests =
  testList "GetCommentsForPost" [
    test "the comments of a post come oldest first" {
      let newest = comment (Guid.NewGuid().ToString()) "2026-02-03T00:00:00.0000000Z" CommentApproval.Rejected
      let oldest = comment (Guid.NewGuid().ToString()) "2026-02-01T00:00:00.0000000Z" CommentApproval.Approved
      let middle = comment (Guid.NewGuid().ToString()) "2026-02-02T00:00:00.0000000Z" CommentApproval.New
      let dependencies = FakePostCommentsDependencies (olderPost, [ newest; oldest; middle ])

      Expect.equal
        (getCommentsForPost dependencies |> Result.map (Option.map (List.map _.Comment)))
        (Ok (Some [ oldest; middle; newest ]))
        "Expected the oldest comment first"
    }

    test "the comments of a post give the rendered HTML" {
      let posted = commentWith markdown (Guid.NewGuid().ToString()) "2026-02-01T00:00:00.0000000Z" CommentApproval.New
      let dependencies = FakePostCommentsDependencies (olderPost, [ posted ])

      match getCommentsForPost dependencies with
      | Ok (Some [ rendered ]) ->
        Expect.equal rendered.Html markdownHtml "Expected the HTML of the source"
        Expect.equal rendered.Comment posted "Expected the stored comment"
      | other -> failtestf "Expected one comment, got %A" other
    }
  ]

let private commenterId = "7d1f0c55-3c1b-4a43-9a0e-2b5f6f0d8e21"

let private commenter (status : CommenterStatus) =
  Commenter.create commenterId "commenter@example.com" (Some "Commenter") "2026-01-01T00:00:00.0000000Z" (status.ToString())
  |> ok

type private FakePostCommentDependencies(existing : Commenter, queueFails : bool) =
  let created = ResizeArray<CommentApproval>()
  let queued = ResizeArray<CommentId>()

  new (existing : Commenter) = FakePostCommentDependencies(existing, false)

  member _.Created = List.ofSeq created
  member _.Queued = List.ofSeq queued

  interface PostComment.Dependencies with
    member _.GetPostBySlug _ = CancellableTaskResult.singleton (Some olderPost)
    member _.GetUtcNow () = DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
    member _.GetPostedCommentById _ = CancellableTaskResult.singleton None
    member _.GetCommenterByValidationId _ = CancellableTaskResult.singleton (Some existing)
    member _.CreateComment _ commenter newComment _ approval =
      created.Add approval
      let posted = comment firstId "2026-02-01T00:00:00.0000000Z" approval
      CancellableTaskResult.singleton { posted with Writer = CommentWriter.Commenter commenter; Content = newComment.Content }
    member _.Stage = TwoPoint.Site.Render.Stage.Production
    member _.QueueNewCommentNotice commentId =
      queued.Add commentId

      if queueFails then
        CancellableTaskResult.error { Dependency = Dependency.ServiceBus; Type = Unknown (exn "The queue is down") }
      else
        CancellableTaskResult.singleton ()

let private postCommentWith (content : string) (dependencies : FakePostCommentDependencies) =
  let dto =
    { NewComment.Dto.Post = NewComment.Post (olderPost.Id.ToString())
      NewComment.Dto.ValidationId = NewComment.ValidationId commenterId
      NewComment.Dto.Comment = NewComment.Comment content
      NewComment.Dto.ReplyTo = None }
  (PostComment.withDependencies dependencies dto CancellationToken.None).Result

let private postComment = postCommentWith "A comment"

let private lengthMessage = $"content must be between 1 and {NewComment.maxLength} characters long"

let private postCommentTests =
  testList "PostComment" [
    test "a comment of a trusted commenter is approved at the time of the post" {
      let dependencies = FakePostCommentDependencies (commenter CommenterStatus.Trusted)

      match postComment dependencies with
      | Ok (PostComment.Event.Posted (_, posted)) ->
        Expect.equal posted.Comment.Status.Approval CommentApproval.Approved "Expected an approved comment"
        Expect.equal dependencies.Created [ CommentApproval.Approved ] "Expected one approved comment in the store"
      | other -> failtestf "Expected Posted, got %A" other
    }

    test "a comment of a new commenter is new" {
      let dependencies = FakePostCommentDependencies (commenter CommenterStatus.New)

      match postComment dependencies with
      | Ok (PostComment.Event.Posted (_, posted)) ->
        Expect.equal posted.Comment.Status.Approval CommentApproval.New "Expected a new comment"
        Expect.equal dependencies.Created [ CommentApproval.New ] "Expected one new comment in the store"
      | other -> failtestf "Expected Posted, got %A" other
    }

    test "a posted comment goes on the new_comment queue" {
      let dependencies = FakePostCommentDependencies (commenter CommenterStatus.New)

      match postComment dependencies with
      | Ok (PostComment.Event.Posted (_, posted)) ->
        Expect.equal dependencies.Queued [ posted.Comment.Id ] "Expected the posted comment on the queue"
      | other -> failtestf "Expected Posted, got %A" other
    }

    test "a failed queue leaves the comment stored and the reply successful" {
      let dependencies = FakePostCommentDependencies (commenter CommenterStatus.New, true)

      match postComment dependencies with
      | Ok (PostComment.Event.Posted (_, posted)) ->
        Expect.equal dependencies.Created [ CommentApproval.New ] "Expected the comment in the store"
        Expect.equal dependencies.Queued [ posted.Comment.Id ] "Expected one try to queue"
      | other -> failtestf "Expected Posted, got %A" other
    }

    test "the answer of a posted comment gives the rendered HTML" {
      let dependencies = FakePostCommentDependencies (commenter CommenterStatus.Trusted)

      match postCommentWith markdown dependencies with
      | Ok (PostComment.Event.Posted (_, posted)) ->
        Expect.equal posted.Html markdownHtml "Expected the HTML of the source"
        Expect.equal (posted.Comment.Content.ToString()) markdown "Expected the stored source"
      | other -> failtestf "Expected Posted, got %A" other
    }

    test "a comment of 1,000 characters is posted" {
      let dependencies = FakePostCommentDependencies (commenter CommenterStatus.New)
      let content = String('x', NewComment.maxLength)

      match postCommentWith content dependencies with
      | Ok (PostComment.Event.Posted (_, posted)) ->
        Expect.equal (posted.Comment.Content.ToString()) content "Expected the whole source in the store"
      | other -> failtestf "Expected Posted, got %A" other
    }

    test "a comment of 1,001 characters is refused, and the blog stores nothing and notifies no one" {
      let dependencies = FakePostCommentDependencies (commenter CommenterStatus.New)

      match postCommentWith (String('x', NewComment.maxLength + 1)) dependencies with
      | Error (ActionError.Validation errors) ->
        Expect.equal errors [ lengthMessage ] "Expected the length message"
        Expect.isEmpty dependencies.Created "Expected no comment in the store"
        Expect.isEmpty dependencies.Queued "Expected no message on the queue"
      | other -> failtestf "Expected a validation error, got %A" other
    }

    test "a comment of only spaces and new lines is refused, and the blog stores nothing and notifies no one" {
      let dependencies = FakePostCommentDependencies (commenter CommenterStatus.New)

      match postCommentWith "  \n " dependencies with
      | Error (ActionError.Validation errors) ->
        Expect.equal errors [ NewComment.blankMessage ] "Expected the blank message"
        Expect.isEmpty dependencies.Created "Expected no comment in the store"
        Expect.isEmpty dependencies.Queued "Expected no message on the queue"
      | other -> failtestf "Expected a validation error, got %A" other
    }

    test "the limit counts the marks of the source as typed" {
      let dependencies = FakePostCommentDependencies (commenter CommenterStatus.New)
      let content = String.replicate 143 "**abc**"
      Expect.equal content.Length (NewComment.maxLength + 1) "Expected a source one character over the limit"

      match postCommentWith content dependencies with
      | Error (ActionError.Validation errors) ->
        Expect.equal errors [ lengthMessage ] "Expected the length message"
        Expect.isEmpty dependencies.Created "Expected no comment in the store"
      | other -> failtestf "Expected a validation error, got %A" other
    }

    test "a banned commenter posts, and the blog stores nothing and notifies no one" {
      let dependencies = FakePostCommentDependencies (commenter CommenterStatus.Banned)

      match postComment dependencies with
      | Error (Logic (PostComment.Banned _)) ->
        Expect.isEmpty dependencies.Created "Expected no comment in the store, so no notification"
        Expect.isEmpty dependencies.Queued "Expected no message on the queue"
      | other -> failtestf "Expected Banned, got %A" other
    }
  ]

let private previewComment (content : string) =
  PreviewComment.preview TwoPoint.Site.Render.Stage.Production { Content = content }

let private previewCommentTests =
  testList "PreviewComment" [
    test "the preview gives the HTML of the source" {
      Expect.equal (previewComment markdown) (Ok markdownHtml) "Expected the HTML of the source"
    }

    test "the preview of 1,000 characters gives the HTML" {
      Expect.isOk (previewComment (String('x', NewComment.maxLength))) "Expected the HTML"
    }

    test "the preview refuses an empty source" {
      Expect.equal (previewComment "") (Error [ lengthMessage ]) "Expected the length message"
    }

    test "the preview refuses a source of only spaces and new lines" {
      Expect.equal (previewComment "  \n ") (Error [ NewComment.blankMessage ]) "Expected the blank message"
    }

    test "the preview refuses a source of 1,001 characters" {
      Expect.equal (previewComment (String('x', NewComment.maxLength + 1))) (Error [ lengthMessage ]) "Expected the length message"
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
  let commentReads = ResizeArray<Commenter>()

  member _.Updates = List.ofSeq updates
  member _.Approvals = List.ofSeq approvals
  member _.CommentReads = List.ofSeq commentReads

  interface UpdateCommenterStatus.Dependencies with
    member _.GetCommenterByValidationId _ = CancellableTaskResult.singleton existing
    member _.GetCommentsOfCommenter commenter =
      commentReads.Add commenter
      CancellableTaskResult.singleton comments
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

    test "a missing commenter gets no update, and the action reads no comment" {
      let dependencies = FakeStatusDependencies (None, commentsOfCommenter)

      match updateStatus dependencies CommenterStatus.Banned with
      | Error (Logic (UpdateCommenterStatus.CommenterNotFound _)) ->
        Expect.isEmpty dependencies.Updates "Expected no update"
        Expect.isEmpty dependencies.CommentReads "Expected no read of the comments"
        Expect.isEmpty dependencies.Approvals "Expected no comment change"
      | other -> failtestf "Expected CommenterNotFound, got %A" other
    }
  ]

let private otherCommenterId = "8e1f0c55-3c1b-4a43-9a0e-2b5f6f0d8e22"

let private commentOf (validationId : string) (id : string) (createdDate : string) =
  let posted = comment id createdDate CommentApproval.Approved
  let commenter = Comment.commenter posted |> Option.get
  { posted with
      Writer = CommentWriter.Commenter { commenter with ValidationId = CommenterValidationId.create validationId |> ok } }

type private FakeCommenterStore(commenters : Commenter list, comments : Comment list, postedAfterFirstRead : Comment list) =
  let commenters = ResizeArray commenters
  let comments = ResizeArray comments
  let postedAfterFirstRead = ResizeArray postedAfterFirstRead
  let sent = ResizeArray<Commenter>()

  new(commenters, comments) = FakeCommenterStore(commenters, comments, [])

  member _.Commenters = List.ofSeq commenters
  member _.Comments = List.ofSeq comments
  member _.Sent = List.ofSeq sent

  interface DeleteCommenter.Dependencies with
    member _.GetCommenterByValidationId validationId =
      commenters |> Seq.tryFind (fun commenter -> commenter.ValidationId = validationId) |> CancellableTaskResult.singleton
    member _.GetCommentsOfCommenter commenter =
      let read =
        comments
        |> Seq.filter (fun comment ->
          Comment.commenter comment |> Option.exists (fun writer -> writer.ValidationId = commenter.ValidationId))
        |> List.ofSeq
      comments.AddRange postedAfterFirstRead
      postedAfterFirstRead.Clear()
      CancellableTaskResult.singleton read
    member _.DeleteComment comment =
      comments.RemoveAll(fun stored -> stored.Id = comment.Id) |> ignore
      CancellableTaskResult.singleton ()
    member _.DeleteCommenter commenter =
      commenters.RemoveAll(fun stored -> stored.ValidationId = commenter.ValidationId) |> ignore
      CancellableTaskResult.singleton ()

  interface ValidateCommenter.Dependencies with
    member _.GetCommenterByEmailAddress emailAddress =
      commenters |> Seq.tryFind (fun commenter -> commenter.EmailAddress = emailAddress) |> CancellableTaskResult.singleton
    member _.GetValidRedirectUris () = CancellableTaskResult.singleton [ Uri "https://twopoint.dev" ]
    member _.CreateCommenter emailAddress name status =
      let created =
        Commenter.create otherCommenterId (emailAddress.ToString()) (name |> Option.map _.ToString()) "2026-03-01T00:00:00.0000000Z" (status.ToString())
        |> ok
      commenters.Add created
      CancellableTaskResult.singleton created
    member _.SendCommenterValidationEmail commenter _ =
      sent.Add commenter
      CancellableTaskResult.singleton "operation"

let private deleteCommenter (store : FakeCommenterStore) =
  let dto = { CommenterDeletion.Dto.ValidationId = CommenterDeletion.ValidationId commenterId }
  (DeleteCommenter.withDependencies store dto CancellationToken.None).Result

let private validateAgain (store : FakeCommenterStore) =
  let dto =
    { CommenterValidation.Dto.EmailAddress = CommenterValidation.EmailAddress "commenter@example.com"
      CommenterValidation.Dto.Name = Some (CommenterValidation.Name "Commenter")
      CommenterValidation.Dto.RedirectUri = CommenterValidation.RedirectUri "https://twopoint.dev/posts/older-post" }
  (ValidateCommenter.withDependencies store dto CancellationToken.None).Result

let private otherComment = commentOf "9f1f0c55-3c1b-4a43-9a0e-2b5f6f0d8e23" "4c6c1f0e-2f0a-4c8e-9a51-6d7c2b1e3f44" "2026-02-04T00:00:00.0000000Z"

let private deleteTests =
  testList "DeleteCommenter" [
    test "a delete removes each comment of the commenter, and the address can verify again" {
      let store = FakeCommenterStore ([ commenter CommenterStatus.Banned ], commentsOfCommenter @ [ otherComment ])

      match deleteCommenter store with
      | Ok (DeleteCommenter.Event.Deleted deleted) ->
        Expect.equal deleted.ValidationId (commenter CommenterStatus.Banned).ValidationId "Expected the deleted commenter"
        Expect.isEmpty store.Commenters "Expected no commenter in the store"
        Expect.equal store.Comments [ otherComment ] "Expected only the comment of another commenter"
      | other -> failtestf "Expected Deleted, got %A" other

      match validateAgain store with
      | Ok (ValidateCommenter.Event.Validated verified) ->
        Expect.equal verified.Status CommenterStatus.New "Expected a new commenter"
        Expect.notEqual verified.ValidationId (commenter CommenterStatus.Banned).ValidationId "Expected a new validation ID"
        Expect.equal store.Sent [ verified ] "Expected one verification email"
      | other -> failtestf "Expected Validated, got %A" other
    }

    test "a comment that the commenter posts during the delete is also removed" {
      let posted = commentOf commenterId "5c6c1f0e-2f0a-4c8e-9a51-6d7c2b1e3f45" "2026-02-05T00:00:00.0000000Z"
      let store = FakeCommenterStore ([ commenter CommenterStatus.New ], commentsOfCommenter @ [ otherComment ], [ posted ])

      match deleteCommenter store with
      | Ok (DeleteCommenter.Event.Deleted _) ->
        Expect.equal store.Comments [ otherComment ] "Expected only the comment of another commenter"
      | other -> failtestf "Expected Deleted, got %A" other
    }

    test "a missing commenter gets no delete" {
      let store = FakeCommenterStore ([], [ otherComment ])

      match deleteCommenter store with
      | Error (Logic (DeleteCommenter.CommenterNotFound _)) ->
        Expect.equal store.Comments [ otherComment ] "Expected each comment to stay"
      | other -> failtestf "Expected CommenterNotFound, got %A" other
    }
  ]

let private commenterAt (validationId : string) (createdDate : string) =
  Commenter.create validationId $"{validationId}@example.com" None createdDate "new" |> ok

type private FakeCommentersDependencies(commenters : Commenter list) =
  interface GetAllCommenters.Dependencies with
    member _.GetAllCommenters () = CancellableTaskResult.singleton commenters

type private FakeCommenterDependencies(existing : Commenter option, comments : PostedComment list) =
  interface GetCommenter.Dependencies with
    member _.GetCommenterByValidationId _ = CancellableTaskResult.singleton existing
    member _.GetCommentsOfCommenter _ = CancellableTaskResult.singleton comments
    member _.Stage = TwoPoint.Site.Render.Stage.Production

let private getCommenter (dependencies : FakeCommenterDependencies) =
  (GetCommenter.withDependencies dependencies commenterId CancellationToken.None).Result

let private commenterQueryTests =
  testList "Commenter queries" [
    test "the commenters come newest first" {
      let oldest = commenterAt "1e1f0c55-3c1b-4a43-9a0e-2b5f6f0d8e21" "2026-01-01T00:00:00.0000000Z"
      let newest = commenterAt "2e1f0c55-3c1b-4a43-9a0e-2b5f6f0d8e21" "2026-01-03T00:00:00.0000000Z"
      let middle = commenterAt "3e1f0c55-3c1b-4a43-9a0e-2b5f6f0d8e21" "2026-01-02T00:00:00.0000000Z"

      Expect.equal
        (GetAllCommenters.withDependencies (FakeCommentersDependencies [ oldest; newest; middle ]) () CancellationToken.None).Result
        (Ok [ newest; middle; oldest ])
        "Expected the newest commenter first"
    }

    test "a commenter comes with each comment, oldest first" {
      let newest = queued olderPost "2026-02-03T00:00:00.0000000Z"
      let oldest = queued latestPost "2026-02-01T00:00:00.0000000Z"

      Expect.equal
        (getCommenter (FakeCommenterDependencies (Some (commenter CommenterStatus.Trusted), [ newest; oldest ])))
        (Ok (Some { Commenter = commenter CommenterStatus.Trusted; Comments = [ oldest; newest ] |> List.map rendered }))
        "Expected the oldest comment first"
    }

    test "the commenter page gives the rendered HTML" {
      let posted = queuedWith markdown olderPost "2026-02-01T00:00:00.0000000Z"

      match getCommenter (FakeCommenterDependencies (Some (commenter CommenterStatus.Trusted), [ posted ])) with
      | Ok (Some { Comments = [ shown ] }) ->
        Expect.equal shown.Comment.Html markdownHtml "Expected the HTML of the source"
        Expect.equal shown.Comment.Comment posted.Comment "Expected the stored comment"
      | other -> failtestf "Expected one comment, got %A" other
    }

    test "a missing commenter is none" {
      Expect.equal (getCommenter (FakeCommenterDependencies (None, []))) (Ok None) "Expected no commenter"
    }
  ]

[<Tests>]
let tests =
  testList "Moderation" [
    approvalTests
    queueTests
    postCommentsTests
    postCommentTests
    previewCommentTests
    validationTests
    statusTests
    deleteTests
    commenterQueryTests
  ]
