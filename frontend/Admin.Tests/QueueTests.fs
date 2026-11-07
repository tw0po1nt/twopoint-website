module TwoPoint.Admin.Tests.QueueTests

open TwoPoint.Admin

open Expecto

let private queued id (name: string) status : Api.PostedComment =
  { PostSlug = "first-post"
    PostTitle = "First post"
    Comment =
      { Id = id
        CreatedDate = "2026-02-01T00:00:00Z"
        Writer =
          Api.Writer.Commenter
            { Id = $"{name.ToLower()}-id"
              Email = $"{name.ToLower()}@example.com"
              Name = name
              Status = status
              CreatedDate = "2026-01-01T00:00:00Z" }
        Status = Api.CommentApproval.New
        Content = "A comment"
        ReplyTo = None
        TopLevel = None
        ReplyCount = 0 } }

let private first = queued "first" "Ada" Api.New
let private second = queued "second" "Grace" Api.Trusted
let private third = queued "third" "Ada" Api.New

let private send msg model = Shell.update (Shell.Queue msg) model

let private loadedWith comments =
  let model, _ = Shell.init "/comments"
  let model, _ = Shell.update (Shell.Session(Session.Started true)) model
  let model, _ = send (Queue.Answered(Ok comments)) model
  model

let private loaded () = loadedWith [ first; second ]

let private trusted (comment: Api.PostedComment) =
  match comment.Comment.Writer with
  | Api.Writer.Commenter commenter ->
    { comment with
        Comment.Writer = Api.Writer.Commenter { commenter with Status = Api.Trusted } }
  | Api.Writer.Author -> comment

let private commenterStatus commentId (model: Shell.Model) =
  Queue.tryFind commentId model.Queue
  |> Option.bind (fun row ->
    match row.Comment.Comment.Writer with
    | Api.Writer.Commenter commenter -> Some commenter.Status
    | Api.Writer.Author -> None)

let private replyOf (comment: Api.PostedComment) =
  { comment with
      Comment.ReplyTo = Some "top-level" }

let private reply msg model = send (Queue.Reply msg) model

let private typed (commentId: string) (text: string) model =
  reply (AuthorReply.Edited(commentId, TwoPoint.Site.Widgets.Comments.TextChanged text)) model

let private answer (commentId: string) (approved: string list) : Api.AuthorReply =
  { Reply =
      { Id = "answer"
        CreatedDate = "2026-03-01T00:00:00Z"
        Writer = Api.Writer.Author
        Status = Api.CommentApproval.Approved
        Content = "An answer"
        ReplyTo = Some commentId
        TopLevel = None
        ReplyCount = 0 }
    Approved = approved }

let private replyTests =
  testList "the reply as the author" [
    test "a new row offers the reply with the approval in its label, and its form tells the author about the approval" {
      Expect.isTrue (AuthorReply.canReply None Api.CommentApproval.New) "A new row takes a reply"
      Expect.equal (AuthorReply.actionLabel Api.CommentApproval.New) "Reply and approve" "The label names the approval"
      Expect.equal
        (AuthorReply.approvalNote Api.CommentApproval.New)
        (Some "Posting your reply also approves this comment.")
        "The note of a new row"
    }

    test "the reply opens a form under its row, and a second reply moves the form to that row" {
      let model, effect = reply (AuthorReply.Opened "first") (loaded ())
      Expect.isNone effect "No call at the open"
      Expect.isTrue (AuthorReply.isOpenOn "first" model.Queue.Reply) "The form is open on the first row"

      let model, _ = reply (AuthorReply.Opened "second") model
      Expect.isTrue (AuthorReply.isOpenOn "second" model.Queue.Reply) "One form at a time"
    }

    test "the preview of a reply asks the backend for the HTML" {
      let model, _ = reply (AuthorReply.Opened "first") (loaded ())
      let model, _ = typed "first" "An *answer*" model
      let _, effect = reply (AuthorReply.Edited("first", TwoPoint.Site.Widgets.Comments.PreviewSelected)) model

      match effect with
      | Some(Shell.Send [ Shell.PreviewReply(Shell.ReplyPage.OnQueue, "first", request) ]) ->
        Expect.equal request.Source "An *answer*" "The text of the reply"
      | other -> failtestf "Expected the preview call, got %A" other
    }

    test "an empty reply sends nothing" {
      let model, _ = reply (AuthorReply.Opened "first") (loaded ())
      let model, _ = typed "first" "   " model
      let _, effect = reply AuthorReply.Submitted model
      Expect.isNone effect "No call"
    }

    test "a posted reply on a new row removes the approved row and lowers the navigation count" {
      let model, _ = reply (AuthorReply.Opened "first") (loaded ())
      let model, _ = typed "first" "An answer" model
      let model, effect = reply AuthorReply.Submitted model
      Expect.equal effect (Some(Shell.Send [ Shell.PostReply(Shell.ReplyPage.OnQueue, "first-post", "first", "An answer") ])) "The reply call"

      let model, effect = reply (AuthorReply.Answered("first", Ok(answer "first" [ "first" ]))) model
      Expect.equal effect (Some(Shell.SetBadge 1)) "The badge shows the new count"
      Expect.isNone (Queue.tryFind "first" model.Queue) "The approved row goes away"
      Expect.isNone model.Queue.Reply "The form closes"
    }

    test "a reply on a reply row posts to that row, and the backend moves it to the top-level comment" {
      let model, _ = reply (AuthorReply.Opened "first") (loadedWith [ replyOf first; second ])
      let model, _ = typed "first" "An answer" model
      let _, effect = reply AuthorReply.Submitted model
      Expect.equal effect (Some(Shell.Send [ Shell.PostReply(Shell.ReplyPage.OnQueue, "first-post", "first", "An answer") ])) "The reply call"
    }

    test "a failed reply keeps the form and its text, and shows the reason" {
      let model, _ = reply (AuthorReply.Opened "first") (loaded ())
      let model, _ = typed "first" "An answer" model
      let model, _ = reply AuthorReply.Submitted model
      let model, _ = reply (AuthorReply.Answered("first", Error(Api.Failed "This comment is rejected, so you can't reply to it."))) model
      let form = model.Queue.Reply |> Option.get
      Expect.equal form.Editor.Text "An answer" "The text stays"
      Expect.equal (AuthorReply.failure form) (Some "Couldn't post the reply. This comment is rejected, so you can't reply to it.") "The reason"
      Expect.equal (Queue.count model.Queue) (Some 2) "The rows stay"
    }

    test "Cancel closes the form" {
      let model, _ = reply (AuthorReply.Opened "first") (loaded ())
      let model, _ = reply AuthorReply.Canceled model
      Expect.isNone model.Queue.Reply "The form closes"
    }

    test "a reload of the queue keeps the open form" {
      let model, _ = reply (AuthorReply.Opened "first") (loaded ())
      let model, _ = typed "first" "An answer" model
      let model, _ = send (Queue.Answered(Ok [ first; second; third ])) model
      Expect.equal (model.Queue.Reply |> Option.map _.Editor.Text) (Some "An answer") "The draft stays"
    }

    test "a refused token on a reply offers another account" {
      let model, _ = reply (AuthorReply.Opened "first") (loaded ())
      let model, _ = typed "first" "An answer" model
      let model, _ = reply AuthorReply.Submitted model
      let model, _ = reply (AuthorReply.Answered("first", Error Api.Refused)) model
      Expect.equal model.Session.Status Session.Refused "The banner offers another account"
    }
  ]

let private topLevel approval : Api.TopLevel =
  { Id = "top-level"
    Writer =
      Api.Writer.Commenter
        { Id = "grace-id"
          Email = "grace@example.com"
          Name = "Grace"
          Status = Api.Trusted
          CreatedDate = "2026-01-01T00:00:00Z" }
    Status = approval
    Excerpt = "Why does List.map keep the order?"
    Content = "<p>Why does <code>List.map</code> keep the order?</p>" }

let private under approval (comment: Api.PostedComment) =
  { comment with
      Comment.ReplyTo = Some "top-level"
      Comment.TopLevel = Some(topLevel approval) }

let private rowTopLevel commentId (model: Shell.Model) =
  Queue.tryFind commentId model.Queue |> Option.bind _.Comment.Comment.TopLevel

let private contextTests =
  testList "the reply context" [
    test "a reply row names the top-level comment and shows its excerpt" {
      let model = loadedWith [ under Api.CommentApproval.Approved first; second ]
      let context = rowTopLevel "first" model |> Option.get
      Expect.equal (ReplyContext.replyLine context) "Reply to Grace" "The reply line"
      Expect.equal context.Excerpt "Why does List.map keep the order?" "The excerpt"
      Expect.isNone (rowTopLevel "second" model) "A top-level comment has no reply line"
    }

    test "the excerpt opens the full text of the top-level comment, and a second click closes it" {
      let model = loadedWith [ under Api.CommentApproval.Approved first; under Api.CommentApproval.Approved third ]
      Expect.isEmpty model.Queue.Opened "Only the excerpt at first"

      let model, effect = send (Queue.TopLevelToggled "first") model
      Expect.isNone effect "No call"
      Expect.equal model.Queue.Opened (set [ "first" ]) "The full text of the first row only"
      Expect.equal (rowTopLevel "first" model |> Option.map _.Content) (Some "<p>Why does <code>List.map</code> keep the order?</p>") "The full HTML"

      let model, _ = send (Queue.TopLevelToggled "first") model
      Expect.isEmpty model.Queue.Opened "The excerpt again"
    }

    test "a reply under a rejected top-level comment tells the author that readers won't see it" {
      let model = loadedWith [ under Api.CommentApproval.Rejected first; under Api.CommentApproval.Approved third ]
      Expect.equal
        (rowTopLevel "first" model |> Option.bind ReplyContext.rejectedNote)
        (Some "Readers won't see this reply, because Grace's comment is rejected.")
        "The note under a rejected comment"
      Expect.isNone (rowTopLevel "third" model |> Option.bind ReplyContext.rejectedNote) "No note under an approved comment"
      Expect.isNone (ReplyContext.rejectedNote (topLevel Api.CommentApproval.New)) "No note under a new comment"
    }

    test "a reply under a rejected top-level comment offers no reply, and an open asks for nothing" {
      let model = loadedWith [ under Api.CommentApproval.Rejected first ]
      let model, effect = reply (AuthorReply.Opened "first") model
      Expect.isNone model.Queue.Reply "No form"
      Expect.isNone effect "No call"
    }

    test "a reject of a top-level comment in the queue gives the note to each reply under it" {
      let topLevelRow = { queued "top-level" "Grace" Api.Trusted with PostTitle = "First post" }
      let model = loadedWith [ topLevelRow; under Api.CommentApproval.New first ]
      let model, _ = send (Queue.Chosen("top-level", Queue.Reject)) model
      let model, _ = send (Queue.Done("top-level", Queue.Reject, Ok())) model
      Expect.equal (rowTopLevel "first" model |> Option.map _.Status) (Some Api.CommentApproval.Rejected) "The reply knows of the reject"

      let model = loadedWith [ topLevelRow; under Api.CommentApproval.New first ]
      let model, _ = send (Queue.Chosen("top-level", Queue.Approve)) model
      let model, _ = send (Queue.Done("top-level", Queue.Approve, Ok())) model
      Expect.equal (rowTopLevel "first" model |> Option.map _.Status) (Some Api.CommentApproval.Approved) "The reply knows of the approval"
    }

    test "the queue stays oldest first, with no group by thread" {
      let model = loadedWith [ under Api.CommentApproval.Approved first; second; under Api.CommentApproval.Approved third ]

      match model.Queue.Comments with
      | Queue.Loaded rows -> Expect.equal (rows |> List.map _.Comment.Comment.Id) [ "first"; "second"; "third" ] "The order of the answer"
      | other -> failtestf "Expected the rows, got %A" other
    }
  ]

let private deleteTests =
  testList "the delete of a thread in the queue" [
    test "the delete dialog gives the number of replies that go with the comment" {
      let topLevel = queued "top-level" "Ada" Api.New
      let model = loadedWith [ { topLevel with Comment.ReplyCount = 1 }; replyOf second ]
      let model, _ = Shell.update (Shell.Opened(Shell.DeleteDialog "top-level")) model
      Expect.equal (Shell.deleteReplyCount model) (Some 1) "One reply"

      Expect.equal
        (Shell.deleteReplyCount model |> Option.map Queue.deleteText)
        (Some
          "This deletes the comment, its reply, and their history. To hide the thread from readers but keep it, reject the comment instead. This can't be undone.")
        "The text names the reply"

      let model, _ = Shell.update (Shell.Opened(Shell.DeleteDialog "second")) model
      Expect.equal (Shell.deleteReplyCount model) (Some 0) "A reply has none"
    }

    test "the delete of a top-level comment removes its replies from the queue" {
      let topLevel = queued "top-level" "Ada" Api.New
      let model = loadedWith [ { topLevel with Comment.ReplyCount = 1 }; replyOf second; first ]
      let model, _ = Shell.update (Shell.Opened(Shell.DeleteDialog "top-level")) model
      let model, _ = Shell.update (Shell.deleteChosen model).Value model
      let model, _ = send (Queue.Done("top-level", Queue.Delete, Ok())) model
      Expect.isNone (Queue.tryFind "second" model.Queue) "The reply goes"
      Expect.equal (Queue.count model.Queue) (Some 1) "Only the other comment stays"
    }
  ]

[<Tests>]
let tests =
  testList "Queue" [
    contextTests
    deleteTests

    test "the navigation count is the number of comments in the queue, after the queue loads" {
      let model, _ = Shell.init "/comments"
      Expect.isNone (Queue.count model.Queue) "No count before the answer"
      Expect.equal (Queue.count (loaded ()).Queue) (Some 2) "Two new comments"
    }

    test "Approve sends the approval, and the answer removes the row and lowers the navigation count" {
      let model, effect = send (Queue.Chosen("first", Queue.Approve)) (loaded ())
      Expect.equal effect (Some(Shell.Send [ Shell.Moderate(first, Queue.Approve) ])) "The approval call"
      Expect.isTrue (Queue.tryFind "first" model.Queue |> Option.exists (fun row -> Progress.isSending row.State)) "The buttons are off"
      Expect.equal (Queue.count model.Queue) (Some 2) "The count stays until the answer"

      let model, effect = send (Queue.Done("first", Queue.Approve, Ok())) model
      Expect.equal effect (Some(Shell.SetBadge 1)) "The badge shows the new count"
      Expect.isNone (Queue.tryFind "first" model.Queue) "The row goes away"
      Expect.equal (Queue.count model.Queue) (Some 1) "One new comment"
    }

    test "Reject sends the rejection, and the answer removes the row and lowers the navigation count" {
      let model, effect = send (Queue.Chosen("second", Queue.Reject)) (loaded ())
      Expect.equal effect (Some(Shell.Send [ Shell.Moderate(second, Queue.Reject) ])) "The rejection call"
      let model, _ = send (Queue.Done("second", Queue.Reject, Ok())) model
      Expect.isNone (Queue.tryFind "second" model.Queue) "The row goes away"
      Expect.equal (Queue.count model.Queue) (Some 1) "One new comment"
    }

    test "a row of a new commenter offers Approve and trust, and a row of another commenter does not" {
      Expect.equal (Queue.actions Api.New) [ Queue.Approve; Queue.ApproveAndTrust; Queue.Reject; Queue.Delete ] "New"
      Expect.equal (Queue.actions Api.Trusted) [ Queue.Approve; Queue.Reject; Queue.Delete ] "Trusted"
      Expect.equal (Queue.actions Api.Banned) [ Queue.Approve; Queue.Reject; Queue.Delete ] "Banned"
    }

    test "Approve and trust sends the trust, then the approval, and the answer removes the row" {
      let model, effect = send (Queue.Chosen("first", Queue.ApproveAndTrust)) (loadedWith [ first; second; third ])
      let ada =
        match first.Comment.Writer with
        | Api.Writer.Commenter commenter -> commenter
        | Api.Writer.Author -> failtest "Expected a commenter"

      Expect.equal effect (Some(Shell.Send [ Shell.TrustCommenter(first, ada) ])) "The trust call"
      Expect.equal (commenterStatus "third" model) (Some Api.New) "The commenter stays new until the answer"

      let model, effect = send (Queue.Trusted("first", Ok())) model
      Expect.equal effect (Some(Shell.Send [ Shell.Moderate(trusted first, Queue.Approve) ])) "The approval call"
      Expect.equal (commenterStatus "third" model) (Some Api.Trusted) "The other comment of the commenter shows the trust"
      Expect.equal (commenterStatus "second" model) (Some Api.Trusted) "The other commenter stays as it was"
      Expect.isTrue (Queue.tryFind "first" model.Queue |> Option.exists (fun row -> Progress.isSending row.State)) "The buttons stay off"

      let model, effect = send (Queue.Done("first", Queue.Approve, Ok())) model
      Expect.equal effect (Some(Shell.SetBadge 2)) "The badge shows the new count"
      Expect.isNone (Queue.tryFind "first" model.Queue) "The row goes away"
      Expect.equal (Queue.count model.Queue) (Some 2) "Two new comments"
    }

    test "Approve and trust on a row of a trusted commenter sends nothing" {
      let _, effect = send (Queue.Chosen("second", Queue.ApproveAndTrust)) (loaded ())
      Expect.isNone effect "No call"
    }

    test "a refused trust sends no approval, and keeps the row and the commenter status, with the reason" {
      let model, _ = send (Queue.Chosen("first", Queue.ApproveAndTrust)) (loadedWith [ first; third ])
      let model, effect = send (Queue.Trusted("first", Error(Api.Failed "Commenter is banned and cannot become trusted"))) model

      Expect.isNone effect "No approval call"
      Expect.equal
        (Queue.tryFind "first" model.Queue |> Option.bind (fun row -> Queue.failure row.State))
        (Some "Couldn't trust the commenter, so the comment wasn't approved. Commenter is banned and cannot become trusted")
        "The reason"
      Expect.equal (commenterStatus "third" model) (Some Api.New) "The commenter stays new"
    }

    test "a failed approval after the trust shows the trust, and the author can approve again" {
      let model, _ = send (Queue.Chosen("first", Queue.ApproveAndTrust)) (loadedWith [ first; third ])
      let model, _ = send (Queue.Trusted("first", Ok())) model
      let model, _ = send (Queue.Done("first", Queue.Approve, Error Api.Unanswered)) model

      Expect.equal
        (Queue.tryFind "first" model.Queue |> Option.bind (fun row -> Queue.failure row.State))
        (Some "Couldn't approve the comment. The server didn't respond. Check your connection and try again.")
        "The reason"
      Expect.equal (commenterStatus "first" model) (Some Api.Trusted) "The row shows the trust"
      Expect.equal (commenterStatus "third" model) (Some Api.Trusted) "The other comment of the commenter shows the trust"

      let _, effect = send (Queue.Chosen("first", Queue.Approve)) model
      Expect.equal effect (Some(Shell.Send [ Shell.Moderate(trusted first, Queue.Approve) ])) "The approval call again"
    }

    test "a refused token on the trust offers another account" {
      let model, _ = send (Queue.Chosen("first", Queue.ApproveAndTrust)) (loaded ())
      let model, _ = send (Queue.Trusted("first", Error Api.Refused)) model
      Expect.equal model.Session.Status Session.Refused "The banner offers another account"
    }

    test "a second click while the action is sent sends nothing" {
      let model, _ = send (Queue.Chosen("first", Queue.Approve)) (loaded ())
      let _, effect = send (Queue.Chosen("first", Queue.Reject)) model
      Expect.isNone effect "One call"
    }

    test "a failed action keeps the row with the reason, and the author can try again" {
      let model, _ = send (Queue.Chosen("first", Queue.Approve)) (loaded ())
      let model, _ = send (Queue.Done("first", Queue.Approve, Error(Api.Failed "Comment is approved and cannot become approved"))) model
      let row = Queue.tryFind "first" model.Queue
      Expect.equal
        (row |> Option.bind (fun row -> Queue.failure row.State))
        (Some "Couldn't approve the comment. Comment is approved and cannot become approved")
        "The reason"
      Expect.equal (Queue.count model.Queue) (Some 2) "The count stays"
      let _, effect = send (Queue.Chosen("first", Queue.Approve)) model
      Expect.equal effect (Some(Shell.Send [ Shell.Moderate(first, Queue.Approve) ])) "The approval call again"
    }

    test "Delete opens the confirmation and sends nothing" {
      let model, effect = Shell.update (Shell.Opened(Shell.DeleteDialog "first")) (loaded ())
      Expect.isNone effect "No call before the confirmation"
      Expect.equal model.Overlay (Some(Shell.DeleteDialog "first")) "The confirmation is open"
    }

    test "a delete with no confirmation sends nothing" {
      let model, effect = send (Queue.Chosen("first", Queue.Delete)) (loaded ())
      Expect.isNone effect "No call"
      Expect.equal (Queue.count model.Queue) (Some 2) "The row stays"
    }

    test "Delete in the confirmation closes it, sends the delete, and the answer removes the row" {
      let model, _ = Shell.update (Shell.Opened(Shell.DeleteDialog "first")) (loaded ())
      let model, effect = send (Queue.Chosen("first", Queue.Delete)) model
      Expect.isNone model.Overlay "The confirmation closes"
      Expect.equal effect (Some(Shell.Send [ Shell.Moderate(first, Queue.Delete) ])) "The delete call"
      let model, _ = send (Queue.Done("first", Queue.Delete, Ok())) model
      Expect.equal (Queue.count model.Queue) (Some 1) "One new comment"
    }

    test "Cancel in the confirmation closes it and keeps the row" {
      let model, _ = Shell.update (Shell.Opened(Shell.DeleteDialog "first")) (loaded ())
      let model, effect = Shell.update (Shell.Closed(Shell.DeleteDialog "first")) model
      Expect.isNone model.Overlay "The confirmation closes"
      Expect.isNone effect "No call"
      Expect.equal (Queue.count model.Queue) (Some 2) "The row stays"
    }

    test "a queue that did not load shows the reason" {
      let model, _ = Shell.init "/comments"
      let model, _ = send (Queue.Answered(Error Api.Unanswered)) model
      Expect.equal model.Queue.Comments (Queue.NotLoaded "The server didn't respond. Check your connection and try again.") "The reason"
    }

    test "a refused token offers another account" {
      let model, _ = send (Queue.Chosen("first", Queue.Approve)) (loaded ())
      let model, _ = send (Queue.Done("first", Queue.Approve, Error Api.Refused)) model
      Expect.equal model.Session.Status Session.Refused "The banner offers another account"
    }

    replyTests
  ]
