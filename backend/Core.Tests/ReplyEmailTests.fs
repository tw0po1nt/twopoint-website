module TwoPoint.Core.Tests.ReplyEmailTests

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Shared
open TwoPoint.Core.Tests.Fixtures
open TwoPoint.Core.Util

open Expecto
open FsToolkit.ErrorHandling

open System
open System.Text.RegularExpressions
open System.Threading

let private now = DateTime(2026, 3, 1, 9, 30, 0, DateTimeKind.Utc)
let private site = Uri "https://twopoint.dev/"
let private stopToken = Guid "4e3d2c1b-0a9f-4e8d-8c7b-6a5f4e3d2c1b"

let private topLevelId = "0b6c1f0e-2f0a-4c8e-9a51-6d7c2b1e3f40"
let private replyId = "5a0e2c1d-8b3f-4e6a-9c7d-1f2e3d4c5b6a"
let private authorReplyId = "3c2b1a0f-9e8d-4c7b-8a6f-5e4d3c2b1a0f"
let private adaId = "1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d"
let private benId = "2b3c4d5e-6f7a-4b8c-9d0e-1f2a3b4c5d6e"

let private commenterOf (validationId : string) (name : string) (status : CommenterStatus) =
  Commenter.create validationId "commenter@example.com" (Some name) "2026-01-01T00:00:00.0000000Z" (status.ToString())
  |> ok

let private ada =
  Commenter.create adaId "ada@example.com" (Some "Ada") "2026-01-01T00:00:00.0000000Z" "trusted" |> ok

let private ben = commenterOf benId "Ben" CommenterStatus.Trusted

let private consent = { ConsentDate = now.AddDays -30.0; StopToken = ReplyEmailStopToken.ofGuid stopToken; StopDate = None }

let private commentOf
  (id : string)
  (parent : string option)
  (writer : CommentWriter)
  (approval : CommentApproval)
  (replyEmails : ReplyEmailConsent option)
  (content : string)
  =
  let createdDate = "2026-02-01T00:00:00.0000000Z"
  Comment.create id createdDate {| Approval = approval.ToString(); CreatedDate = createdDate |} parent writer replyEmails content
  |> ok

let private topLevelWith (writer : Commenter) (approval : CommentApproval) (replyEmails : ReplyEmailConsent option) =
  commentOf topLevelId None (CommentWriter.Commenter writer) approval replyEmails "How does `List.map` work?"

let private topLevel = topLevelWith ada CommentApproval.Approved (Some consent)

let private replyFrom (writer : CommentWriter) (approval : CommentApproval) =
  commentOf replyId (Some topLevelId) writer approval None "Use `List.map f xs`.\n\n> a quote\n\n- one\n- two"

let private reply = replyFrom (CommentWriter.Commenter ben) CommentApproval.Approved

let private authorReply =
  commentOf authorReplyId (Some topLevelId) CommentWriter.Author CommentApproval.Approved None "It maps each item."

let private postedOn (post : Post) (comment : Comment) =
  { PostedComment.Post = post.Id; PostTitle = post.Info.Title; Comment = comment }

let private posted = postedOn olderPost

let private sentRecord = ReplyEmailRecord.Sent (now.AddMinutes -5.0)

let private gateTests =
  testList "the gate" [
    test "a first approval is a change from new to approved, or a reply that its writer posts as approved" {
      Expect.isTrue (ReplyEmailRule.isFirstApproval (Some CommentApproval.New) reply) "Expected a first approval from new"
      Expect.isTrue (ReplyEmailRule.isFirstApproval None reply) "Expected a first approval at the post"
    }

    test "a new approval after a reject is not a first approval" {
      Expect.isFalse (ReplyEmailRule.isFirstApproval (Some CommentApproval.Rejected) reply) "Expected no first approval"
    }

    test "the approval of a top-level comment, and a reply that is not approved, are not a first approval" {
      Expect.isFalse (ReplyEmailRule.isFirstApproval (Some CommentApproval.New) topLevel) "Expected no first approval of a top-level comment"

      for approval in [ CommentApproval.New; CommentApproval.Rejected ] do
        Expect.isFalse
          (ReplyEmailRule.isFirstApproval (Some CommentApproval.New) (replyFrom (CommentWriter.Commenter ben) approval))
          $"Expected no first approval of a {approval} reply"
    }

    test "an approved reply from someone else to an approved top-level comment with a consent passes, to the commenter" {
      match ReplyEmailRule.check (Some (posted reply)) (Some (posted topLevel)) ReplyEmailRecord.NotSent with
      | Ok send ->
        Expect.equal send.Recipient ada "Expected the commenter of the top-level comment"
        Expect.equal send.StopToken consent.StopToken "Expected the stop token of the consent"
        Expect.equal send.Reply reply "Expected the reply"
        Expect.equal send.PostTitle olderPost.Info.Title "Expected the post title"
      | Error reason -> failtestf "Expected a send, got %A" reason
    }

    test "a reply from the author passes" {
      Expect.isOk (ReplyEmailRule.check (Some (posted authorReply)) (Some (posted topLevel)) ReplyEmailRecord.NotSent) "Expected a send"
    }

    let bannedAda = Commenter.create adaId "ada@example.com" (Some "Ada") "2026-01-01T00:00:00.0000000Z" "banned" |> ok

    let skips =
      [ "a parent that is new", reply, topLevelWith ada CommentApproval.New (Some consent), ReplyEmailRecord.NotSent, ReplyEmailSkip.TopLevelNotApproved
        "a parent that is rejected", reply, topLevelWith ada CommentApproval.Rejected (Some consent), ReplyEmailRecord.NotSent, ReplyEmailSkip.TopLevelNotApproved
        "no consent", reply, topLevelWith ada CommentApproval.Approved None, ReplyEmailRecord.NotSent, ReplyEmailSkip.NoConsent
        "a stop", reply, topLevelWith ada CommentApproval.Approved (Some { consent with StopDate = Some now }), ReplyEmailRecord.NotSent, ReplyEmailSkip.Stopped
        "a reply from the commenter of the top-level comment", replyFrom (CommentWriter.Commenter ada) CommentApproval.Approved, topLevel, ReplyEmailRecord.NotSent, ReplyEmailSkip.OwnReply
        "a send record", reply, topLevel, sentRecord, ReplyEmailSkip.AlreadySent
        "a ban at the send", reply, topLevelWith bannedAda CommentApproval.Approved (Some consent), ReplyEmailRecord.NotSent, ReplyEmailSkip.RecipientBanned
        "a reply that is rejected at the send", replyFrom (CommentWriter.Commenter ben) CommentApproval.Rejected, topLevel, ReplyEmailRecord.NotSent, ReplyEmailSkip.ReplyNotApproved
        "a top-level comment", topLevel, topLevel, ReplyEmailRecord.NotSent, ReplyEmailSkip.NotAReply ]

    for name, reply, topLevel, record, reason in skips do
      test $"{name} sends no reply email" {
        Expect.equal (ReplyEmailRule.check (Some (posted reply)) (Some (posted topLevel)) record) (Error reason) "Expected the reason"
      }

    test "a missing reply or a missing top-level comment sends no reply email" {
      Expect.equal (ReplyEmailRule.check None (Some (posted topLevel)) ReplyEmailRecord.NotSent) (Error ReplyEmailSkip.ReplyMissing) "Expected a missing reply"
      Expect.equal (ReplyEmailRule.check (Some (posted reply)) None ReplyEmailRecord.NotSent) (Error ReplyEmailSkip.TopLevelMissing) "Expected a missing top-level comment"

      Expect.equal
        (ReplyEmailRule.check (Some (posted reply)) (Some (postedOn latestPost topLevel)) ReplyEmailRecord.NotSent)
        (Error ReplyEmailSkip.TopLevelMissing)
        "Expected a top-level comment on another post to be missing"
    }
  ]

/// Each call that changes the store or the queue, in order.
type private Effect =
  | Stored of CommentId * CommentApproval
  | Queued of Slug * CommentId

let private queueDown = { Dependency = Dependency.ServiceBus; Type = Unknown (exn "The queue is down") }

type private FakeComments(commenter : Commenter, stored : PostedComment list, queueFails : bool) =
  let effects = ResizeArray<Effect>()

  let queue post reply =
    effects.Add(Queued(post, reply))
    if queueFails then CancellableTaskResult.error queueDown else CancellableTaskResult.singleton ()

  let approve (comment : Comment) approval =
    effects.Add(Stored(comment.Id, approval))
    CancellableTaskResult.singleton { comment with Status = { comment.Status with Approval = approval } }

  let find post id = stored |> List.tryFind (fun (posted : PostedComment) -> posted.Post = post && posted.Comment.Id = id)

  new(commenter, stored) = FakeComments(commenter, stored, false)

  member _.Effects = List.ofSeq effects
  member _.Queued = effects |> Seq.choose (function Queued(post, reply) -> Some(post, reply) | Stored _ -> None) |> List.ofSeq

  interface PostComment.Dependencies with
    member _.GetPostBySlug _ = CancellableTaskResult.singleton (Some olderPost)
    member _.GetUtcNow () = now
    member _.GetPostedComment post id = find post id |> CancellableTaskResult.singleton
    member _.GetCommenterByValidationId _ = CancellableTaskResult.singleton (Some commenter)
    member _.NewCommentId () = CommentId.ofGuid (Guid replyId)
    member _.CreateComment id _ commenter newComment topLevel approval replyEmails =
      effects.Add(Stored(id, approval))
      commentOf (string id) (topLevel |> Option.map (_.Id >> string)) (CommentWriter.Commenter commenter) approval replyEmails (newComment.Content.ToString())
      |> CancellableTaskResult.singleton
    member _.NewReplyEmailStopToken () = ReplyEmailStopToken.ofGuid stopToken
    member _.CreateReplyEmailStop _ = CancellableTaskResult.singleton ()
    member _.QueueNewCommentNotice _ _ = CancellableTaskResult.singleton ()
    member _.QueueReplyEmail post reply = queue post reply
    member _.Stage = TwoPoint.Site.Render.Stage.Production

  interface ReplyAsAuthor.Dependencies with
    member _.GetPostedComment post id = find post id |> CancellableTaskResult.singleton
    member _.UpdateCommentApproval comment approval = approve comment approval
    member _.CreateAuthorReply _ topLevel content approval =
      effects.Add(Stored(CommentId.create authorReplyId |> ok, approval))
      commentOf authorReplyId (Some (string topLevel.Id)) CommentWriter.Author approval None (content.ToString())
      |> CancellableTaskResult.singleton
    member _.QueueReplyEmail post reply = queue post reply
    member _.Stage = TwoPoint.Site.Render.Stage.Production

  interface UpdateCommentApproval.Dependencies with
    member _.GetComment post id = find post id |> Option.map _.Comment |> CancellableTaskResult.singleton
    member _.UpdateCommentApproval comment approval = approve comment approval
    member _.QueueReplyEmail post reply = queue post reply

let private postFrom (dependencies : FakeComments) (replyTo : string option) =
  let dto =
    { NewComment.Dto.Post = NewComment.Post (olderPost.Id.ToString())
      NewComment.Dto.ValidationId = NewComment.ValidationId benId
      NewComment.Dto.Comment = NewComment.Comment "A reply"
      NewComment.Dto.ReplyTo = replyTo |> Option.map NewComment.ReplyTo
      NewComment.Dto.ReplyEmails = NewComment.ReplyEmails false }
  (PostComment.withDependencies dependencies dto CancellationToken.None).Result

let private replyAsAuthor (dependencies : FakeComments) (replyTo : string) =
  let dto = { AuthorReply.Dto.Post = olderPost.Id.ToString(); AuthorReply.Dto.ReplyTo = replyTo; AuthorReply.Dto.Content = "An answer" }
  (ReplyAsAuthor.withDependencies dependencies dto CancellationToken.None).Result

let private setApproval (dependencies : FakeComments) (id : string) (approval : CommentApproval) =
  let dto =
    { CommentApprovalUpdate.Dto.Post = CommentApprovalUpdate.Post (olderPost.Id.ToString())
      CommentApprovalUpdate.Dto.CommentId = CommentApprovalUpdate.CommentId id
      CommentApprovalUpdate.Dto.Approval = CommentApprovalUpdate.Approval (approval.ToString()) }
  (UpdateCommentApproval.withDependencies dependencies dto CancellationToken.None).Result

let private idOf (id : string) = CommentId.create id |> ok

let private queueTests =
  testList "the queue" [
    test "a reply of a trusted commenter puts one message on the queue at the post, after the store" {
      let dependencies = FakeComments(ben, [ posted topLevel ])

      Expect.isOk (postFrom dependencies (Some topLevelId)) "Expected a posted reply"
      Expect.equal
        dependencies.Effects
        [ Stored(idOf replyId, CommentApproval.Approved); Queued(olderPost.Id, idOf replyId) ]
        "Expected the message with the slug and the reply ID"
    }

    test "a reply of a new commenter, and a top-level comment of a trusted commenter, put no message on the queue" {
      let fromNew = FakeComments(commenterOf benId "Ben" CommenterStatus.New, [ posted topLevel ])
      Expect.isOk (postFrom fromNew (Some topLevelId)) "Expected a posted reply"
      Expect.isEmpty fromNew.Queued "Expected no message for a new reply"

      let topLevelPost = FakeComments(ben, [])
      Expect.isOk (postFrom topLevelPost None) "Expected a posted comment"
      Expect.isEmpty topLevelPost.Queued "Expected no message for a top-level comment"
    }

    test "an author reply puts one message on the queue" {
      let dependencies = FakeComments(ben, [ posted topLevel ])

      Expect.isOk (replyAsAuthor dependencies topLevelId) "Expected a reply"
      Expect.equal dependencies.Queued [ olderPost.Id, idOf authorReplyId ] "Expected the author reply on the queue"
    }

    test "an author reply to a new reply puts only the reply and the author reply on the queue" {
      let dependencies =
        FakeComments(ben, [ posted (topLevelWith ada CommentApproval.New (Some consent)); posted (replyFrom (CommentWriter.Commenter ben) CommentApproval.New) ])

      Expect.isOk (replyAsAuthor dependencies replyId) "Expected a reply"
      Expect.equal
        dependencies.Effects
        [ Stored(idOf topLevelId, CommentApproval.Approved)
          Stored(idOf replyId, CommentApproval.Approved)
          Queued(olderPost.Id, idOf replyId)
          Stored(idOf authorReplyId, CommentApproval.Approved)
          Queued(olderPost.Id, idOf authorReplyId) ]
        "Expected a message after each first approval of a reply"
    }

    test "the approval of a new reply in the queue puts one message on the queue" {
      let dependencies = FakeComments(ben, [ posted topLevel; posted (replyFrom (CommentWriter.Commenter ben) CommentApproval.New) ])

      Expect.isOk (setApproval dependencies replyId CommentApproval.Approved) "Expected an approval"
      Expect.equal dependencies.Queued [ olderPost.Id, idOf replyId ] "Expected the reply on the queue"
    }

    for name, id, current, requested in
      [ "a new approval of a rejected reply", replyId, CommentApproval.Rejected, CommentApproval.Approved
        "a reject of a new reply", replyId, CommentApproval.New, CommentApproval.Rejected
        "the approval of a new top-level comment", topLevelId, CommentApproval.New, CommentApproval.Approved ] do
      test $"{name} puts no message on the queue" {
        let dependencies =
          FakeComments(ben, [ posted (topLevelWith ada current (Some consent)); posted (replyFrom (CommentWriter.Commenter ben) current) ])

        Expect.isOk (setApproval dependencies id requested) "Expected the change"
        Expect.isEmpty dependencies.Queued "Expected no message"
      }

    test "a failed queue keeps the approval and answers the admin" {
      let dependencies = FakeComments(ben, [ posted topLevel; posted (replyFrom (CommentWriter.Commenter ben) CommentApproval.New) ], true)

      match setApproval dependencies replyId CommentApproval.Approved with
      | Ok (UpdateCommentApproval.Event.Updated updated) ->
        Expect.equal updated.Status.Approval CommentApproval.Approved "Expected an approved reply"
        Expect.equal dependencies.Queued [ olderPost.Id, idOf replyId ] "Expected one try"
      | other -> failtestf "Expected Updated, got %A" other
    }
  ]

let private publishedPost = "---\ntitle: A post\nauthor: Matt Watt\n---\n\nThe body.\n"

type private FakeSend(stored : PostedComment list, blob : string option, sendFails : bool, recordFails : bool) =
  let mutable record = ReplyEmailRecord.NotSent
  let sent = ResizeArray<EmailAddress * ReplyEmailContent>()
  let records = ResizeArray<Slug * CommentId * DateTime>()

  new(stored) = FakeSend(stored, Some publishedPost, false, false)

  member _.Sent = List.ofSeq sent
  member _.Records = List.ofSeq records

  interface SendReplyEmail.Dependencies with
    member _.GetPostedComment post id =
      stored |> List.tryFind (fun posted -> posted.Post = post && posted.Comment.Id = id) |> CancellableTaskResult.singleton
    member _.GetReplyEmailRecord _ _ = CancellableTaskResult.singleton record
    member _.GetPublishedBlob _ =
      blob |> Option.map (fun text -> { StoredBlob.Text = text; ETag = "etag"; Mark = NotMigrated }) |> CancellableTaskResult.singleton
    member _.SendReplyEmail recipient content =
      if sendFails then
        CancellableTaskResult.error { Dependency = Dependency.Communications; Type = Unknown (exn "ACS is down") }
      else
        sent.Add(recipient, content)
        CancellableTaskResult.singleton ()
    member _.RecordReplyEmailSent post reply sentDate =
      if recordFails then
        CancellableTaskResult.error { Dependency = Dependency.Database; Type = Unknown (exn "The table is down") }
      else
        records.Add(post, reply, sentDate)
        record <- ReplyEmailRecord.Sent sentDate
        CancellableTaskResult.singleton ()
    member _.GetUtcNow () = now
    member _.Site = site
    member _.Stage = TwoPoint.Site.Render.Stage.Production

let private sendFor (dependencies : FakeSend) (id : string) =
  (SendReplyEmail.withDependencies dependencies { Post = olderPost.Id.ToString(); ReplyId = id } CancellationToken.None).Result

let private skipped (reason : ReplyEmailSkip) : ActionResult<SendReplyEmail.Event, SendReplyEmail.Error> =
  Ok (SendReplyEmail.Event.Skipped (idOf replyId, reason))

let private sendTests =
  testList "the send" [
    test "a reply that passes the gate sends one email to the commenter, and then writes the send record" {
      let dependencies = FakeSend [ posted topLevel; posted reply ]

      Expect.equal (sendFor dependencies replyId) (Ok (SendReplyEmail.Event.Sent (olderPost.Id, idOf replyId))) "Expected a send"

      match dependencies.Sent with
      | [ recipient, content ] ->
        Expect.equal recipient ada.EmailAddress "Expected the email address of the commenter"
        Expect.equal content.Subject "Ben replied to your comment on A post" "Expected the subject"
      | other -> failtestf "Expected one email, got %A" other

      Expect.equal dependencies.Records [ olderPost.Id, idOf replyId, now ] "Expected the send date on the reply"
    }

    test "the author name of an author reply comes from the published post" {
      let dependencies = FakeSend [ posted topLevel; posted authorReply ]

      sendFor dependencies authorReplyId |> ignore

      Expect.equal
        (dependencies.Sent |> List.map (snd >> _.Subject))
        [ "Matt Watt replied to your comment on A post" ]
        "Expected the author of the front matter"
    }

    test "a retry after a send sends no second email" {
      let dependencies = FakeSend [ posted topLevel; posted reply ]

      sendFor dependencies replyId |> ignore

      Expect.equal (sendFor dependencies replyId) (skipped ReplyEmailSkip.AlreadySent) "Expected a skip"
      Expect.equal dependencies.Sent.Length 1 "Expected one email"
      Expect.equal dependencies.Records.Length 1 "Expected one send record"
    }

    test "a ban of the commenter before the send sends no email" {
      let bannedAda = Commenter.create adaId "ada@example.com" (Some "Ada") "2026-01-01T00:00:00.0000000Z" "banned" |> ok
      let dependencies = FakeSend [ posted (topLevelWith bannedAda CommentApproval.Approved (Some consent)); posted reply ]

      Expect.equal (sendFor dependencies replyId) (skipped ReplyEmailSkip.RecipientBanned) "Expected a skip"
      Expect.isEmpty dependencies.Sent "Expected no email"
    }

    test "a reply that is gone at the send sends no email" {
      let dependencies = FakeSend [ posted topLevel ]

      Expect.equal (sendFor dependencies replyId) (skipped ReplyEmailSkip.ReplyMissing) "Expected a skip"
      Expect.isEmpty dependencies.Sent "Expected no email"
    }

    test "a post that is not published sends no email" {
      let dependencies = FakeSend([ posted topLevel; posted reply ], None, false, false)

      Expect.equal (sendFor dependencies replyId) (skipped ReplyEmailSkip.PostNotPublished) "Expected a skip"
      Expect.isEmpty dependencies.Sent "Expected no email"
    }

    test "a failed send writes no send record, so that Service Bus tries again" {
      let dependencies = FakeSend([ posted topLevel; posted reply ], Some publishedPost, true, false)

      match sendFor dependencies replyId with
      | Error (ActionError.Dependency error) -> Expect.equal error.Dependency Dependency.Communications "Expected the ACS error"
      | other -> failtestf "Expected a dependency error, got %A" other

      Expect.isEmpty dependencies.Records "Expected no send record"
    }

    test "a failed write of the send record after a send gives the error in the event, so that Service Bus sends no second email" {
      let dependencies = FakeSend([ posted topLevel; posted reply ], Some publishedPost, false, true)

      match sendFor dependencies replyId with
      | Ok (SendReplyEmail.Event.SentWithNoRecord (_, reply, error)) ->
        Expect.equal reply (idOf replyId) "Expected the reply"
        Expect.equal error.Dependency Dependency.Database "Expected the table error"
      | other -> failtestf "Expected SentWithNoRecord, got %A" other

      Expect.equal dependencies.Sent.Length 1 "Expected one email"
    }

    test "a message with a reply ID that is not a GUID is invalid" {
      match sendFor (FakeSend []) "not-a-guid" with
      | Error (ActionError.Validation _) -> ()
      | other -> failtestf "Expected a validation error, got %A" other
    }
  ]

let private sendOf (reply : Comment) =
  ReplyEmailRule.check (Some (posted reply)) (Some (posted topLevel)) ReplyEmailRecord.NotSent |> ok

let private compose (reply : Comment) =
  ReplyEmailContent.compose TwoPoint.Site.Render.Stage.Production site "Matt Watt" now (sendOf reply)

let private threadUrl = $"https://twopoint.dev/posts/older-post/?thread={topLevelId}#comment-{replyId}"
let private stopUrl = $"https://twopoint.dev/comments/stop/?token={stopToken}"

let private hrefs (html : string) =
  Regex.Matches(html, "href=\"([^\"]*)\"")
  |> Seq.map _.Groups[1].Value
  |> Seq.filter (fun href -> href.StartsWith "http" && not (href.StartsWith "https://fonts."))
  |> Set.ofSeq

let private composeTests =
  testList "the compose function" [
    test "the subject names the commenter who replied and the post" {
      Expect.equal (compose reply).Subject "Ben replied to your comment on A post" "Expected the subject"
    }

    test "the subject of an author reply names the post author, and the body marks the author" {
      let content = compose authorReply

      Expect.equal content.Subject "Matt Watt replied to your comment on A post" "Expected the author name"
      Expect.stringContains content.Html "Matt Watt (author) replied to your comment on" "Expected the author mark in the HTML"
      Expect.stringContains content.Text "Matt Watt (author) replied to your comment on A post:" "Expected the author mark in the text"
    }

    test "the email greets the commenter and links the post title" {
      let content = compose reply

      Expect.stringContains content.Html "Hey Ada!" "Expected the greeting"
      Expect.stringContains content.Html "<a href=\"https://twopoint.dev/posts/older-post/\"" "Expected the post link"
      Expect.stringContains content.Html ">A post</a>:" "Expected the title as the link"
    }

    test "the excerpt is the first 200 characters of the plain text of the top-level comment" {
      let long = String.replicate 30 "word *one* "
      let longTopLevel = { topLevel with Content = NonEmptyString.create None long |> ok }
      let send = ReplyEmailRule.check (Some (posted reply)) (Some (posted longTopLevel)) ReplyEmailRecord.NotSent |> ok
      let content = ReplyEmailContent.compose TwoPoint.Site.Render.Stage.Production site "Matt Watt" now send
      let excerpt = (String.replicate 30 "word one ").Substring(0, 200)

      Expect.stringContains content.Html $"{excerpt}\n" "Expected the excerpt in the block quote"
      Expect.stringContains content.Text $"> {excerpt}\n" "Expected the excerpt in the text"
      Expect.isFalse (content.Text.Contains (String.replicate 23 "word one ")) "Expected no more than the excerpt"
    }

    test "the HTML of the reply comes from the comment renderer, with inline styles" {
      let html = (compose reply).Html

      Expect.stringContains html "<code style=\"" "Expected a style on the code"
      Expect.stringContains html "List.map f xs</code>" "Expected the code"
      Expect.stringContains html "<blockquote style=\"margin: 0 0 16px" "Expected a style on the block quote"
      Expect.stringContains html "<ul style=\"" "Expected a style on the list"
      Expect.stringContains html "<li style=\"margin: 0 0 4px\">one</li>" "Expected a style on each item"
      Expect.isFalse (Regex.IsMatch(html, "<(p|code|pre|blockquote|ul|ol|li)>")) "Expected no tag with no style"
    }

    test "the button opens the reply in its thread" {
      let html = (compose reply).Html

      Expect.stringContains html $"<a href=\"{threadUrl}\"" "Expected the thread link"
      Expect.stringContains html "Read the thread" "Expected the button"
      Expect.stringContains html "Reply on the post" "Expected the line under the button"
    }

    test "the footer says why the email came, with the stop link of the comment" {
      let html = (compose reply).Html

      Expect.stringContains html "You asked for reply emails when you commented on A post." "Expected the reason"
      Expect.stringContains html $"<a href=\"{stopUrl}\"" "Expected the stop link"
      Expect.stringContains html "Stop reply emails for this comment" "Expected the stop text"
      Expect.stringContains html "2026 TwoPoint. All rights reserved." "Expected the copyright line"
    }

    test "the plain-text part has the same parts, with the reply as plain text" {
      Expect.equal
        (compose reply).Text
        (String.concat
          "\n"
          [ "Hey Ada!"
            ""
            "Ben replied to your comment on A post:"
            ""
            "> How does List.map work?"
            ""
            "Use List.map f xs. a quote one two"
            ""
            $"Read the thread: {threadUrl}"
            "Reply on the post"
            ""
            "---"
            "You asked for reply emails when you commented on A post."
            $"Stop reply emails for this comment: {stopUrl}"
            ""
            "© 2026 TwoPoint. All rights reserved."
            "" ])
        "Expected the plain-text part"
    }

    test "the email has no commercial content and no link to another post" {
      let content = compose reply

      for text in [ content.Html; content.Text ] do
        for word in [ "subscribe"; "newsletter"; "thanks" ] do
          Expect.isFalse (text.ToLowerInvariant().Contains word) $"Expected no '{word}'"

      Expect.equal
        (hrefs content.Html)
        (set [ "https://twopoint.dev/"; "https://twopoint.dev/posts/older-post/"; threadUrl; stopUrl ])
        "Expected only the site, the post, the thread, and the stop link"
    }

    test "the names and the title are HTML-encoded" {
      let mallory = commenterOf benId "<b>Mallory</b>" CommenterStatus.Trusted
      let html = (compose (replyFrom (CommentWriter.Commenter mallory) CommentApproval.Approved)).Html

      Expect.stringContains html "&lt;b&gt;Mallory&lt;/b&gt; replied" "Expected the encoded name"
      Expect.isFalse (html.Contains "<b>Mallory</b>") "Expected no raw HTML of the name"
    }
  ]

let private flagTests =
  testList "whether the commenter gets reply emails" [
    test "the commenter of a top-level comment with a consent, no stop, and no ban gets reply emails" {
      Expect.isTrue (ReplyEmailRule.getsReplyEmails topLevel) "Expected the flag"
      Expect.isTrue (ReplyEmailRule.getsReplyEmails (topLevelWith ada CommentApproval.New (Some consent))) "Expected the flag on a new comment"
    }

    let bannedAda = commenterOf adaId "Ada" CommenterStatus.Banned

    let noFlags =
      [ "no consent", topLevelWith ada CommentApproval.Approved None
        "a stop", topLevelWith ada CommentApproval.Approved (Some { consent with StopDate = Some now })
        "a ban", topLevelWith bannedAda CommentApproval.Approved (Some consent)
        "a reply", commentOf replyId (Some topLevelId) (CommentWriter.Commenter ada) CommentApproval.Approved (Some consent) "A reply" ]

    for name, comment in noFlags do
      test $"a comment with {name} gives no flag" {
        Expect.isFalse (ReplyEmailRule.getsReplyEmails comment) "Expected no flag"
      }
  ]

[<Tests>]
let tests =
  testList "Reply emails" [
    gateTests
    flagTests
    queueTests
    sendTests
    composeTests
  ]
