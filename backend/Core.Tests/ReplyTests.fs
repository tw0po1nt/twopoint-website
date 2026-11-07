module TwoPoint.Core.Tests.ReplyTests

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Posts.Queries
open TwoPoint.Core.Tests.Fixtures
open TwoPoint.Core.Util

open Expecto
open FsToolkit.ErrorHandling

open System
open System.Threading

let private now = DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
let private upcomingPost = post "upcoming-post" "2026-04-01T00:00:00.0000000Z"

let private topLevelId = "0b6c1f0e-2f0a-4c8e-9a51-6d7c2b1e3f40"
let private replyId = "5a0e2c1d-8b3f-4e6a-9c7d-1f2e3d4c5b6a"
let private otherReplyId = "9e8d7c6b-5a4f-4e3d-8c2b-1a0f9e8d7c6b"
let private authorReplyId = "3c2b1a0f-9e8d-4c7b-8a6f-5e4d3c2b1a0f"
let private commenterId = "7d1f0c55-3c1b-4a43-9a0e-2b5f6f0d8e21"

let private commenter (name : string) (status : CommenterStatus) =
  Commenter.create commenterId "commenter@example.com" (Some name) "2026-01-01T00:00:00.0000000Z" (status.ToString())
  |> ok

let private commentWith (id : string) (parent : string option) (writer : CommentWriter) (approval : CommentApproval) (createdDate : string) =
  Comment.create id createdDate {| Approval = approval.ToString(); CreatedDate = createdDate |} parent writer "A comment"
  |> ok

let private fromAda = CommentWriter.Commenter (commenter "Ada" CommenterStatus.Trusted)
let private fromBen = CommentWriter.Commenter (commenter "Ben" CommenterStatus.Trusted)

let private topLevel (approval : CommentApproval) =
  commentWith topLevelId None fromAda approval "2026-02-01T00:00:00.0000000Z"

let private replyOf (approval : CommentApproval) =
  commentWith replyId (Some topLevelId) fromBen approval "2026-02-02T00:00:00.0000000Z"

let private postedOn (post : Post) (comment : Comment) =
  { PostedComment.Post = post.Id; PostTitle = post.Info.Title; Comment = comment }

type private FakePostReply(post : Post, existing : Commenter, stored : PostedComment list) =
  let created = ResizeArray<Comment option * CommentApproval>()
  let queued = ResizeArray<CommentId>()

  member _.Created = List.ofSeq created
  member _.Queued = List.ofSeq queued

  interface PostComment.Dependencies with
    member _.GetPostBySlug _ = CancellableTaskResult.singleton (Some post)
    member _.GetUtcNow () = now
    member _.GetPostedCommentById id =
      stored |> List.tryFind (fun comment -> comment.Comment.Id = id) |> CancellableTaskResult.singleton
    member _.GetCommenterByValidationId _ = CancellableTaskResult.singleton (Some existing)
    member _.CreateComment _ commenter newComment topLevel approval =
      created.Add(topLevel, approval)

      Comment.create
        otherReplyId
        "2026-03-01T00:00:00.0000000Z"
        {| Approval = approval.ToString(); CreatedDate = "2026-03-01T00:00:00.0000000Z" |}
        (topLevel |> Option.map (_.Id >> string))
        (CommentWriter.Commenter commenter)
        (newComment.Content.ToString())
      |> ok
      |> CancellableTaskResult.singleton
    member _.QueueNewCommentNotice commentId =
      queued.Add commentId
      CancellableTaskResult.singleton ()
    member _.Stage = TwoPoint.Site.Render.Stage.Production

let private postReplyWith (content : string) (replyTo : string) (dependencies : FakePostReply) =
  let dto =
    { NewComment.Dto.Post = NewComment.Post (olderPost.Id.ToString())
      NewComment.Dto.ValidationId = NewComment.ValidationId commenterId
      NewComment.Dto.Comment = NewComment.Comment content
      NewComment.Dto.ReplyTo = Some (NewComment.ReplyTo replyTo) }
  (PostComment.withDependencies dependencies dto CancellationToken.None).Result

let private postReply = postReplyWith "A reply" topLevelId

let private replyCheckTests =
  testList "the reply check" [
    let refusals =
      [ "a reply to a reply", olderPost, replyId, [ postedOn olderPost (topLevel CommentApproval.Approved); postedOn olderPost (replyOf CommentApproval.Approved) ], ReplyRefusal.ReplyToReply
        "a reply to a new comment", olderPost, topLevelId, [ postedOn olderPost (topLevel CommentApproval.New) ], ReplyRefusal.ReplyToNew
        "a reply to a rejected comment", olderPost, topLevelId, [ postedOn olderPost (topLevel CommentApproval.Rejected) ], ReplyRefusal.ReplyToRejected
        "a reply to a missing comment", olderPost, topLevelId, [], ReplyRefusal.ReplyToMissing
        "a reply to a comment on another post", olderPost, topLevelId, [ postedOn latestPost (topLevel CommentApproval.Approved) ], ReplyRefusal.ReplyToMissing
        "an upcoming post", upcomingPost, topLevelId, [ postedOn upcomingPost (topLevel CommentApproval.Approved) ], ReplyRefusal.PostUpcoming ]

    for name, post, replyTo, stored, reason in refusals do
      test $"{name} refuses the reply with its reason, and the blog stores nothing and notifies no one" {
        let dependencies = FakePostReply(post, commenter "Ben" CommenterStatus.Trusted, stored)

        Expect.equal (postReplyWith "A reply" replyTo dependencies) (Error (Logic (PostComment.ReplyRefused reason))) "Expected the reason"
        Expect.isEmpty dependencies.Created "Expected no comment in the store"
        Expect.isEmpty dependencies.Queued "Expected no message on the queue"
      }

    test "an approved top-level comment of the same post takes a reply" {
      let dependencies = FakePostReply(olderPost, commenter "Ben" CommenterStatus.Trusted, [ postedOn olderPost (topLevel CommentApproval.Approved) ])

      match postReply dependencies with
      | Ok (PostComment.Event.Posted (_, posted)) ->
        Expect.equal (posted.Comment.Parent |> Option.map string) (Some topLevelId) "Expected the top-level comment as the parent"
        Expect.equal (dependencies.Created |> List.map (fst >> Option.map (_.Id >> string))) [ Some topLevelId ] "Expected the top-level comment in the store"
        Expect.equal dependencies.Queued [ posted.Comment.Id ] "Expected the reply on the new_comment queue"
      | other -> failtestf "Expected Posted, got %A" other
    }

    test "a reply to an ID that is not a GUID is refused" {
      let dependencies = FakePostReply(olderPost, commenter "Ben" CommenterStatus.Trusted, [])

      match postReplyWith "A reply" "not-a-guid" dependencies with
      | Error (ActionError.Validation _) -> Expect.isEmpty dependencies.Created "Expected no comment in the store"
      | other -> failtestf "Expected a validation error, got %A" other
    }

    test "a reply of 1,001 characters is refused, and the blog stores nothing" {
      let dependencies = FakePostReply(olderPost, commenter "Ben" CommenterStatus.Trusted, [ postedOn olderPost (topLevel CommentApproval.Approved) ])

      match postReplyWith (String('x', NewComment.maxLength + 1)) topLevelId dependencies with
      | Error (ActionError.Validation errors) ->
        Expect.equal errors [ $"content must be between 1 and {NewComment.maxLength} characters long" ] "Expected the length message"
        Expect.isEmpty dependencies.Created "Expected no comment in the store"
      | other -> failtestf "Expected a validation error, got %A" other
    }
  ]

let private approvalTests =
  testList "the approval of a reply" [
    for status, approval in [ CommenterStatus.New, CommentApproval.New; CommenterStatus.Trusted, CommentApproval.Approved ] do
      test $"a reply of a {status} commenter is {approval}" {
        let dependencies = FakePostReply(olderPost, commenter "Ben" status, [ postedOn olderPost (topLevel CommentApproval.Approved) ])

        match postReply dependencies with
        | Ok (PostComment.Event.Posted (_, posted)) ->
          Expect.equal posted.Comment.Status.Approval approval "Expected the approval of the commenter status"
          Expect.equal (dependencies.Created |> List.map snd) [ approval ] "Expected one reply in the store"
        | other -> failtestf "Expected Posted, got %A" other
      }

    test "the blog keeps no reply from a banned commenter" {
      let dependencies = FakePostReply(olderPost, commenter "Ben" CommenterStatus.Banned, [ postedOn olderPost (topLevel CommentApproval.Approved) ])

      match postReply dependencies with
      | Error (Logic (PostComment.Banned _)) ->
        Expect.isEmpty dependencies.Created "Expected no reply in the store"
        Expect.isEmpty dependencies.Queued "Expected no message on the queue"
      | other -> failtestf "Expected Banned, got %A" other
    }
  ]

type private FakeThreads(post : Post, initial : Comment list) =
  let mutable comments = initial
  let updates = ResizeArray<CommentId * CommentApproval>()

  member _.Updates = List.ofSeq updates
  member _.Comments = comments

  interface UpdateCommentApproval.Dependencies with
    member _.GetCommentById id = comments |> List.tryFind (fun comment -> comment.Id = id) |> CancellableTaskResult.singleton
    member _.UpdateCommentApproval comment approval =
      updates.Add(comment.Id, approval)
      let updated = { comment with Status = { comment.Status with Approval = approval } }
      comments <- comments |> List.map (fun existing -> if existing.Id = comment.Id then updated else existing)
      CancellableTaskResult.singleton updated

  interface GetCommentsForPost.Dependencies with
    member _.GetPostBySlug _ = CancellableTaskResult.singleton (Some post)
    member _.GetCommentsForPost approvals _ =
      comments |> List.filter (fun comment -> approvals |> List.contains comment.Status.Approval) |> CancellableTaskResult.singleton
    member _.Stage = TwoPoint.Site.Render.Stage.Production

let private setApproval (threads : FakeThreads) (id : string) (approval : CommentApproval) =
  let dto =
    { CommentApprovalUpdate.Dto.CommentId = CommentApprovalUpdate.CommentId id
      CommentApprovalUpdate.Dto.Approval = CommentApprovalUpdate.Approval (approval.ToString()) }
  (UpdateCommentApproval.withDependencies threads dto CancellationToken.None).Result |> ok |> ignore

let private publicComments (threads : FakeThreads) =
  (GetPublicComments.withDependencies threads (olderPost.Id.ToString()) CancellationToken.None).Result |> ok |> Option.get

let private visibleIds (threads : FakeThreads) = publicComments threads |> List.map _.Id

let private visibilityTests =
  testList "visibility" [
    test "a reject of a top-level comment hides its replies and changes no reply, and a new approval shows the thread again" {
      let threads = FakeThreads(olderPost, [ topLevel CommentApproval.Approved; replyOf CommentApproval.Approved ])
      Expect.equal (visibleIds threads) [ topLevelId; replyId ] "Expected the thread"

      setApproval threads topLevelId CommentApproval.Rejected
      Expect.isEmpty (visibleIds threads) "Expected no reply of a rejected top-level comment"
      Expect.equal (threads.Updates |> List.map (fst >> string)) [ topLevelId ] "Expected a change only to the top-level comment"
      Expect.equal
        (threads.Comments |> List.find (fun comment -> string comment.Id = replyId)).Status.Approval
        CommentApproval.Approved
        "Expected the reply to stay approved"

      setApproval threads topLevelId CommentApproval.Approved
      Expect.equal (visibleIds threads) [ topLevelId; replyId ] "Expected the thread as before"
    }

    test "a reply that is not approved is not visible" {
      for approval in [ CommentApproval.New; CommentApproval.Rejected ] do
        let threads = FakeThreads(olderPost, [ topLevel CommentApproval.Approved; replyOf approval ])
        Expect.equal (visibleIds threads) [ topLevelId ] $"Expected no {approval} reply"
    }

    test "a reply of a new top-level comment is not visible" {
      let threads = FakeThreads(olderPost, [ topLevel CommentApproval.New; replyOf CommentApproval.Approved ])
      Expect.isEmpty (visibleIds threads) "Expected no reply of a new top-level comment"
    }
  ]

let private answerTests =
  testList "the public comments answer" [
    test "each comment gives replyTo and writer, and a reply from the author has no commenter name" {
      let authorReply = commentWith authorReplyId (Some topLevelId) CommentWriter.Author CommentApproval.Approved "2026-02-03T00:00:00.0000000Z"
      let threads = FakeThreads(olderPost, [ authorReply; replyOf CommentApproval.Approved; topLevel CommentApproval.Approved ])

      Expect.equal
        (publicComments threads |> List.map (fun comment -> comment.Id, comment.ReplyTo, comment.Writer, comment.Commenter))
        [ topLevelId, None, "commenter", "Ada"
          replyId, Some topLevelId, "commenter", "Ben"
          authorReplyId, Some topLevelId, "author", "" ]
        "Expected the threads oldest first, with the parent and the writer of each comment"
    }
  ]

/// Each call that changes the store, in order. An author reply has no other effect, so a notification would show here.
type private AuthorReplyEffect =
  | Approved of CommentId
  | CreatedReply of topLevel : CommentId * approval : CommentApproval

type private FakeAuthorReply(stored : PostedComment list) =
  let effects = ResizeArray<AuthorReplyEffect>()

  member _.Effects = List.ofSeq effects

  interface ReplyAsAuthor.Dependencies with
    member _.GetPostedCommentById id =
      stored |> List.tryFind (fun comment -> comment.Comment.Id = id) |> CancellableTaskResult.singleton
    member _.UpdateCommentApproval comment approval =
      effects.Add(Approved comment.Id)
      CancellableTaskResult.singleton { comment with Status = { comment.Status with Approval = approval } }
    member _.CreateAuthorReply _ topLevel content approval =
      effects.Add(CreatedReply(topLevel.Id, approval))

      Comment.create
        authorReplyId
        "2026-03-01T00:00:00.0000000Z"
        {| Approval = approval.ToString(); CreatedDate = "2026-03-01T00:00:00.0000000Z" |}
        (Some (string topLevel.Id))
        CommentWriter.Author
        (content.ToString())
      |> ok
      |> CancellableTaskResult.singleton
    member _.Stage = TwoPoint.Site.Render.Stage.Production

let private replyAsAuthor (replyTo : string) (dependencies : FakeAuthorReply) =
  let dto = { AuthorReply.Dto.Post = olderPost.Id.ToString(); AuthorReply.Dto.ReplyTo = replyTo; AuthorReply.Dto.Content = "An answer" }
  (ReplyAsAuthor.withDependencies dependencies dto CancellationToken.None).Result

let private commentId (id : string) = CommentId.create id |> ok

let private authorReplyTests =
  testList "an author reply" [
    test "an author reply to a new reply approves the reply and its new top-level comment" {
      let dependencies =
        FakeAuthorReply [ postedOn olderPost (topLevel CommentApproval.New); postedOn olderPost (replyOf CommentApproval.New) ]

      match replyAsAuthor replyId dependencies with
      | Ok (ReplyAsAuthor.Event.Replied (_, approved)) ->
        Expect.equal (approved |> List.map (_.Id >> string)) [ topLevelId; replyId ] "Expected both comments in the answer"
        Expect.isTrue (approved |> List.forall (fun comment -> comment.Status.Approval = CommentApproval.Approved)) "Expected approved comments"
        Expect.equal
          dependencies.Effects
          [ Approved (commentId topLevelId); Approved (commentId replyId); CreatedReply (commentId topLevelId, CommentApproval.Approved) ]
          "Expected the approvals before the reply"
      | other -> failtestf "Expected Replied, got %A" other
    }

    test "an author reply on a reply row goes to the top-level comment" {
      let dependencies =
        FakeAuthorReply [ postedOn olderPost (topLevel CommentApproval.Approved); postedOn olderPost (replyOf CommentApproval.Approved) ]

      match replyAsAuthor replyId dependencies with
      | Ok (ReplyAsAuthor.Event.Replied (reply, approved)) ->
        Expect.equal (reply.Comment.Parent |> Option.map string) (Some topLevelId) "Expected the top-level comment as the parent"
        Expect.isEmpty approved "Expected no approval of an approved comment"
        Expect.equal dependencies.Effects [ CreatedReply (commentId topLevelId, CommentApproval.Approved) ] "Expected only the reply"
      | other -> failtestf "Expected Replied, got %A" other
    }

    test "an author reply to an approved reply does not approve its new top-level comment" {
      let dependencies =
        FakeAuthorReply [ postedOn olderPost (topLevel CommentApproval.New); postedOn olderPost (replyOf CommentApproval.Approved) ]

      match replyAsAuthor replyId dependencies with
      | Ok (ReplyAsAuthor.Event.Replied (_, approved)) -> Expect.isEmpty approved "Expected no approval"
      | other -> failtestf "Expected Replied, got %A" other
    }

    test "an author reply to a new top-level comment approves it" {
      let dependencies = FakeAuthorReply [ postedOn olderPost (topLevel CommentApproval.New) ]

      match replyAsAuthor topLevelId dependencies with
      | Ok (ReplyAsAuthor.Event.Replied (_, approved)) ->
        Expect.equal (approved |> List.map (_.Id >> string)) [ topLevelId ] "Expected the top-level comment"
      | other -> failtestf "Expected Replied, got %A" other
    }

    for name, replyTo, stored in
      [ "a rejected top-level comment", topLevelId, [ postedOn olderPost (topLevel CommentApproval.Rejected) ]
        "a rejected reply", replyId, [ postedOn olderPost (topLevel CommentApproval.Approved); postedOn olderPost (replyOf CommentApproval.Rejected) ] ] do
      test $"an author reply to {name} is refused, and the blog stores nothing" {
        let dependencies = FakeAuthorReply stored

        Expect.equal
          (replyAsAuthor replyTo dependencies)
          (Error (Logic (ReplyAsAuthor.Refused AuthorReplyRefusal.ReplyToRejected)))
          "Expected the refusal"
        Expect.isEmpty dependencies.Effects "Expected no change"
      }

    for name, stored in
      [ "a missing comment", []
        "a comment on another post", [ postedOn latestPost (topLevel CommentApproval.Approved) ] ] do
      test $"an author reply to {name} is refused as missing" {
        let dependencies = FakeAuthorReply stored

        Expect.equal
          (replyAsAuthor topLevelId dependencies)
          (Error (Logic (ReplyAsAuthor.Refused AuthorReplyRefusal.ReplyToMissing)))
          "Expected the refusal"
        Expect.isEmpty dependencies.Effects "Expected no change"
      }

    test "an author reply is approved, has no commenter, and sends no notification" {
      let dependencies = FakeAuthorReply [ postedOn olderPost (topLevel CommentApproval.Approved) ]

      match replyAsAuthor topLevelId dependencies with
      | Ok (ReplyAsAuthor.Event.Replied (reply, _)) ->
        Expect.equal reply.Comment.Status.Approval CommentApproval.Approved "Expected an approved reply"
        Expect.equal reply.Comment.Writer CommentWriter.Author "Expected the author as the writer"
        Expect.isNone (Comment.commenter reply.Comment) "Expected no commenter"
        Expect.equal dependencies.Effects [ CreatedReply (commentId topLevelId, CommentApproval.Approved) ] "Expected no other effect"
      | other -> failtestf "Expected Replied, got %A" other
    }

    test "an author reply of 1,001 characters is refused" {
      let dependencies = FakeAuthorReply [ postedOn olderPost (topLevel CommentApproval.Approved) ]
      let dto = { AuthorReply.Dto.Post = olderPost.Id.ToString(); AuthorReply.Dto.ReplyTo = topLevelId; AuthorReply.Dto.Content = String('x', NewComment.maxLength + 1) }

      match (ReplyAsAuthor.withDependencies dependencies dto CancellationToken.None).Result with
      | Error (ActionError.Validation _) -> Expect.isEmpty dependencies.Effects "Expected no change"
      | other -> failtestf "Expected a validation error, got %A" other
    }
  ]

[<Tests>]
let tests = testList "Replies" [ replyCheckTests; approvalTests; visibilityTests; answerTests; authorReplyTests ]
