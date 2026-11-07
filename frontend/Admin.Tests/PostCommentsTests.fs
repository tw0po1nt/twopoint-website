module TwoPoint.Admin.Tests.PostCommentsTests

open TwoPoint.Admin

open Expecto

let private comment id approval : Api.Comment =
  { Id = id
    CreatedDate = "2026-02-01T00:00:00Z"
    Writer =
      Api.Writer.Commenter
        { Id = "ada-id"
          Email = "ada@example.com"
          Name = "Ada"
          Status = Api.New
          CreatedDate = "2026-01-01T00:00:00Z" }
    Status = approval
    Content = "A comment"
    ReplyTo = None
    TopLevel = None
    ReplyCount = 0 }

let private waiting = comment "waiting" Api.CommentApproval.New
let private approved = comment "approved" Api.CommentApproval.Approved
let private rejected = comment "rejected" Api.CommentApproval.Rejected

let private send msg model = Shell.update (Shell.PostComments msg) model

let private start (path: string) =
  let model, _ = Shell.init path
  let model, effect = Shell.update (Shell.Session(Session.Started true)) model
  model, effect

let private loaded () =
  let model, _ = start "/posts/first/comments"
  let model, _ = send (PostComments.Answered("first", Ok [ waiting; approved; rejected ])) model
  model

let private page (model: Shell.Model) = model.PostComments |> Option.get

let private shown (model: Shell.Model) =
  PostComments.shown (page model) |> List.map _.Comment.Id

let private approval (commentId: string) (model: Shell.Model) =
  PostComments.tryFind commentId (page model) |> Option.map _.Comment.Status

let private reply msg model = send (PostComments.Reply msg) model

let private typed (commentId: string) (text: string) model =
  reply (AuthorReply.Edited(commentId, TwoPoint.Site.Widgets.Comments.TextChanged text)) model

let private authorReply : Api.Comment =
  { Id = "answer"
    CreatedDate = "2026-03-01T00:00:00Z"
    Writer = Api.Writer.Author
    Status = Api.CommentApproval.Approved
    Content = "An answer"
    ReplyTo = Some "waiting"
    TopLevel = None
    ReplyCount = 0 }

let private replyTests =
  testList "the reply as the author" [
    test "an approved row offers the reply with no approval note" {
      Expect.isTrue (AuthorReply.canReply None Api.CommentApproval.Approved) "An approved row takes a reply"
      Expect.equal (AuthorReply.actionLabel Api.CommentApproval.Approved) "Reply" "The label"
      Expect.isNone (AuthorReply.approvalNote Api.CommentApproval.Approved) "No note"

      let model, _ = reply (AuthorReply.Opened "approved") (loaded ())
      Expect.isTrue (AuthorReply.isOpenOn "approved" (page model).Reply) "The form is open"
    }

    test "a rejected row offers no reply, and an open asks for nothing" {
      Expect.isFalse (AuthorReply.canReply None Api.CommentApproval.Rejected) "A rejected row takes no reply"
      let model, effect = reply (AuthorReply.Opened "rejected") (loaded ())
      Expect.isNone (page model).Reply "No form"
      Expect.isNone effect "No call"
    }

    test "a reply under a rejected top-level comment offers no reply, and an open asks for nothing" {
      let underRejected = { comment "under-rejected" Api.CommentApproval.Approved with ReplyTo = Some "rejected" }
      let model, _ = start "/posts/first/comments"
      let model, _ = send (PostComments.Answered("first", Ok [ rejected; underRejected ])) model
      Expect.equal (PostComments.topLevelApproval underRejected (page model)) (Some Api.CommentApproval.Rejected) "The top-level approval"
      Expect.isFalse (AuthorReply.canReply (Some Api.CommentApproval.Rejected) Api.CommentApproval.Approved) "No reply"

      let model, effect = reply (AuthorReply.Opened "under-rejected") model
      Expect.isNone (page model).Reply "No form"
      Expect.isNone effect "No call"
    }

    test "a reject of the top-level comment closes the form of a reply under it" {
      let underApproved = { comment "under-approved" Api.CommentApproval.Approved with ReplyTo = Some "approved" }
      let model, _ = start "/posts/first/comments"
      let model, _ = send (PostComments.Answered("first", Ok [ approved; underApproved ])) model
      let model, _ = reply (AuthorReply.Opened "under-approved") model
      Expect.isTrue (AuthorReply.isOpenOn "under-approved" (page model).Reply) "The form is open"

      let model, _ = send (PostComments.Chosen("approved", Queue.Reject)) model
      let model, _ = send (PostComments.Done("approved", Queue.Reject, Ok())) model
      Expect.isNone (page model).Reply "The form closes"
    }

    test "a reject of the row closes its form" {
      let model, _ = reply (AuthorReply.Opened "approved") (loaded ())
      let model, _ = send (PostComments.Chosen("approved", Queue.Reject)) model
      let model, _ = send (PostComments.Done("approved", Queue.Reject, Ok())) model
      Expect.isNone (page model).Reply "The form closes"
    }

    test "a posted reply on a new row approves the row, adds the reply at the end, and lowers the queue count" {
      let model, _ = start "/posts/first/comments"
      let queued: Api.PostedComment = { PostSlug = "first"; PostTitle = "First"; Comment = waiting }
      let model, _ = Shell.update (Shell.Queue(Queue.Answered(Ok [ queued ]))) model
      let model, _ = send (PostComments.Answered("first", Ok [ waiting; approved; rejected ])) model

      let model, _ = reply (AuthorReply.Opened "waiting") model
      let model, _ = typed "waiting" "An answer" model
      let model, effect = reply AuthorReply.Submitted model
      Expect.equal effect (Some(Shell.Send [ Shell.PostReply(Shell.ReplyPage.OnPostComments, "first", "waiting", "An answer") ])) "The reply call"

      let model, effect = reply (AuthorReply.Answered("waiting", Ok { Reply = authorReply; Approved = [ "waiting" ] })) model
      Expect.equal (approval "waiting" model) (Some Api.CommentApproval.Approved) "The row is approved"
      Expect.equal (shown model) [ "waiting"; "approved"; "rejected"; "answer" ] "The reply comes last"
      Expect.equal (Queue.count model.Queue) (Some 0) "The navigation count goes down"
      Expect.equal effect (Some(Shell.SetBadge 0)) "The badge shows the new count"
      Expect.isNone (page model).Reply "The form closes"
    }

    test "a reply from the author shows with the author as the writer" {
      let model, _ = start "/posts/first/comments"
      let model, _ = send (PostComments.Answered("first", Ok [ approved; authorReply ])) model
      let row = PostComments.tryFind "answer" (page model) |> Option.get
      Expect.equal row.Comment.Writer Api.Writer.Author "The author"
      Expect.equal (PostComments.actions row.Comment.Status) [ Queue.Reject; Queue.Delete ] "The actions of an approved row"
    }
  ]

let private replyTo (topLevelId: string) (id: string) approval : Api.Comment =
  { comment id approval with
      ReplyTo = Some topLevelId
      TopLevel =
        Some
          { Id = topLevelId
            Writer = waiting.Writer
            Status = Api.CommentApproval.Approved
            Excerpt = "A comment"
            Content = "<p>A comment</p>" } }

let private threaded () =
  let model, _ = start "/posts/first/comments"

  let model, _ =
    send
      (PostComments.Answered(
        "first",
        Ok
          [ { approved with ReplyCount = 2 }
            replyTo "approved" "first-reply" Api.CommentApproval.Approved
            { waiting with ReplyCount = 1 }
            replyTo "approved" "second-reply" Api.CommentApproval.New
            replyTo "waiting" "third-reply" Api.CommentApproval.Rejected ]
      ))
      model

  model

let private heads (model: Shell.Model) =
  PostComments.heads (page model) |> List.map _.Comment.Id

let private replies (topLevelId: string) (model: Shell.Model) =
  PostComments.replies topLevelId (page model) |> List.map (fun row -> row.Comment.Id, row.Comment.Status)

let private threadTests =
  testList "the threads" [
    test "each thread shows its top-level comment, then its replies oldest first, each with its status" {
      let model = threaded ()
      Expect.equal (heads model) [ "approved"; "waiting" ] "The top-level comments, oldest first"

      Expect.equal
        (replies "approved" model)
        [ "first-reply", Api.CommentApproval.Approved; "second-reply", Api.CommentApproval.New ]
        "The replies of the first thread, oldest first"

      Expect.equal (replies "waiting" model) [ "third-reply", Api.CommentApproval.Rejected ] "The reply of the second thread"
    }

    test "a filter that hides the top-level comment shows the reply in the place of its thread, with the name of the top-level comment" {
      let model, _ = send (PostComments.Filtered(PostComments.Only Api.CommentApproval.New)) (threaded ())
      Expect.equal (heads model) [ "second-reply"; "waiting" ] "The new reply in the place of its thread"
      Expect.isEmpty (replies "waiting" model) "The rejected reply is hidden"

      let reply = PostComments.tryFind "second-reply" (page model) |> Option.get
      Expect.equal (PostComments.topLevel reply.Comment (page model) |> Option.map ReplyContext.replyLine) (Some "Reply to Ada") "The reply line"
    }

    test "a filter that shows the top-level comment keeps the shown replies under it" {
      let model, _ = send (PostComments.Filtered(PostComments.Only Api.CommentApproval.Approved)) (threaded ())
      Expect.equal (heads model) [ "approved" ] "The approved top-level comment"
      Expect.equal (replies "approved" model) [ "first-reply", Api.CommentApproval.Approved ] "The approved reply under it"
    }

    test "a posted reply from the author goes at the end of its thread" {
      let model, _ = reply (AuthorReply.Opened "approved") (threaded ())
      let model, _ = typed "approved" "An answer" model
      let model, _ = reply AuthorReply.Submitted model
      let answer = { authorReply with ReplyTo = Some "approved" }
      let model, _ = reply (AuthorReply.Answered("approved", Ok { Reply = answer; Approved = [] })) model
      Expect.equal (heads model) [ "approved"; "waiting" ] "The threads stay"
      Expect.equal (replies "approved" model |> List.map fst) [ "first-reply"; "second-reply"; "answer" ] "The answer comes last"
    }

    test "the top-level comment of a reply has the approval that it has on the page" {
      let model = threaded ()
      let model, _ = send (PostComments.Chosen("approved", Queue.Reject)) model
      let model, _ = send (PostComments.Done("approved", Queue.Reject, Ok())) model
      let reply = PostComments.tryFind "first-reply" (page model) |> Option.get

      Expect.equal
        (PostComments.topLevel reply.Comment (page model) |> Option.map _.Status)
        (Some Api.CommentApproval.Rejected)
        "The reject on the page"
    }
  ]

/// The reply count and the text of the delete dialog of the comment.
let private deleteDialog (commentId: string) (model: Shell.Model) =
  let model, _ = Shell.update (Shell.Opened(Shell.DeleteDialog commentId)) model
  Shell.deleteReplyCount model, Shell.deleteReplyCount model |> Option.map Queue.deleteText

let private deleted (commentId: string) (model: Shell.Model) =
  let model, _ = Shell.update (Shell.Opened(Shell.DeleteDialog commentId)) model
  let model, _ = Shell.update (Shell.deleteChosen model).Value model
  fst (send (PostComments.Done(commentId, Queue.Delete, Ok())) model)

let private deleteTests =
  testList "the delete of a thread" [
    test "the delete dialog of a top-level comment gives the number of replies that go with it" {
      Expect.equal
        (deleteDialog "approved" (threaded ()))
        (Some 2,
         Some
           "This permanently deletes the comment, its 2 replies, and their history. To hide the thread from readers but keep it, reject the comment instead.")
        "Two replies"

      Expect.equal
        (deleteDialog "waiting" (threaded ()) |> snd)
        (Some
          "This permanently deletes the comment, its 1 reply, and their history. To hide the thread from readers but keep it, reject the comment instead.")
        "One reply"
    }

    test "the delete dialog of a reply names no other comment" {
      Expect.equal
        (deleteDialog "first-reply" (threaded ()))
        (Some 0,
         Some "This permanently deletes the comment and its history. To hide it from readers but keep it, reject it instead.")
        "No reply"
    }

    test "the delete of a top-level comment removes its replies from the page" {
      let model = deleted "approved" (threaded ())
      Expect.equal (heads model) [ "waiting" ] "The other thread stays"
      Expect.isNone (PostComments.tryFind "first-reply" (page model)) "The first reply goes"
      Expect.isNone (PostComments.tryFind "second-reply" (page model)) "The second reply goes"
      Expect.equal (replies "waiting" model) [ "third-reply", Api.CommentApproval.Rejected ] "The reply of the other thread stays"
    }

    test "the delete of a reply lowers the count of its thread" {
      let model = deleted "first-reply" (threaded ())
      Expect.equal (replies "approved" model |> List.map fst) [ "second-reply" ] "The other reply stays"
      Expect.equal (deleteDialog "approved" model |> fst) (Some 1) "One reply left"
    }

    test "a posted reply from the author raises the count of its thread" {
      let model, _ = reply (AuthorReply.Opened "approved") (threaded ())
      let model, _ = typed "approved" "An answer" model
      let model, _ = reply AuthorReply.Submitted model
      let answer = { authorReply with ReplyTo = Some "approved" }
      let model, _ = reply (AuthorReply.Answered("approved", Ok { Reply = answer; Approved = [] })) model
      Expect.equal (deleteDialog "approved" model |> fst) (Some 3) "Three replies"
    }
  ]

[<Tests>]
let tests =
  testList "PostComments" [
    threadTests
    deleteTests

    test "a start on the page of a post loads its comments with the first API calls" {
      let model, effect = start "/posts/first/comments"
      Expect.equal
        effect
        (Some(Shell.Send [ Shell.LoadQueue; Shell.LoadPostComments "first"; Shell.LoadPosts ]))
        "The comments and the posts"
      Expect.equal (page model).Comments PostComments.Waiting "The page waits"
      Expect.equal (Posts.postName "first" model.Posts) Posts.NameWaiting "The post name waits"
    }

    test "the post name is the title from the post list, or the slug when the list has no title" {
      let model, _ = start "/posts/first/comments"
      let post: Api.Post =
        { Slug = "first"
          Title = "The first post"
          State = Api.Published
          PendingEdit = false
          PublishDate = None
          Deploy = Api.DeployState.Deployed
          DeliveryToStart = false
          CommentStats = { New = 0; Approved = 0; Rejected = 0 } }

      let named, _ = Shell.update (Shell.Posts(Posts.Answered(Ok [ post ]))) model
      Expect.equal (Posts.postName "first" named.Posts) (Posts.Name "The first post") "The title"

      let failed, _ = Shell.update (Shell.Posts(Posts.Answered(Error(Api.Failed "No list")))) model
      Expect.equal (Posts.postName "first" failed.Posts) (Posts.Name "first") "The slug after a failed load"

      let other, _ = Shell.update (Shell.Posts(Posts.Answered(Ok [ { post with Slug = "second" } ]))) model
      Expect.equal (Posts.postName "first" other.Posts) (Posts.Name "first") "The slug when the list has no post of the slug"
    }

    test "a link to the page of a post pushes its path and loads its comments" {
      let model, _ = start "/"
      let model, effect = Shell.update (Shell.Router(Router.LinkFollowed "/posts/first/comments")) model
      Expect.equal
        effect
        (Some(Shell.Navigate(Router.PushUrl "/posts/first/comments", [ Shell.LoadPostComments "first"; Shell.LoadPosts ])))
        "The URL, the comments, and the posts"
      Expect.equal (Router.title model.Router.Route) "Comments" "The title"
      Expect.isTrue (Router.isCurrent (Posts PostFilter.All) model.Router.Route) "The posts are the current page in the navigation"
    }

    test "the back button to the page of a post loads its comments again" {
      let model = loaded ()
      let model, _ = Shell.update (Shell.Router(Router.UrlChanged "/comments")) model
      let model, effect = Shell.update (Shell.Router(Router.UrlChanged "/posts/first/comments")) model
      Expect.equal effect (Some(Shell.Send [ Shell.LoadPostComments "first"; Shell.LoadPosts ])) "The comments again"
      Expect.equal (page model).Comments PostComments.Waiting "No old comments"
    }

    test "an answer for another post changes nothing" {
      let model, _ = start "/posts/second/comments"
      let next, _ = send (PostComments.Answered("first", Ok [ waiting ])) model
      Expect.equal next model "The page still waits for its own comments"
    }

    test "the filter shows all comments, or only the new, the approved, or the rejected comments" {
      let model = loaded ()
      Expect.equal (page model).Filter PostComments.All "All at first"
      Expect.equal (shown model) [ "waiting"; "approved"; "rejected" ] "All"

      for approval, expected in
        [ Api.CommentApproval.New, [ "waiting" ]
          Api.CommentApproval.Approved, [ "approved" ]
          Api.CommentApproval.Rejected, [ "rejected" ] ] do
        let model, effect = send (PostComments.Filtered(PostComments.Only approval)) model
        Expect.isNone effect "The filter calls no API"
        Expect.equal (shown model) expected $"Only {approval}"

      let model, _ = send (PostComments.Filtered(PostComments.Only Api.CommentApproval.New)) model
      let model, _ = send (PostComments.Filtered PostComments.All) model
      Expect.equal (shown model) [ "waiting"; "approved"; "rejected" ] "All again"
    }

    test "the filter keeps a comment whose approval changed, and the comment moves to its new filter" {
      let model, _ = send (PostComments.Filtered(PostComments.Only Api.CommentApproval.Approved)) (loaded ())
      let model, _ = send (PostComments.Chosen("approved", Queue.Reject)) model
      let model, _ = send (PostComments.Done("approved", Queue.Reject, Ok())) model
      Expect.equal (shown model) [] "No approved comment"
      let model, _ = send (PostComments.Filtered(PostComments.Only Api.CommentApproval.Rejected)) model
      Expect.equal (shown model) [ "approved"; "rejected" ] "Two rejected comments"
    }

    test "a filter with no comment has its own empty text" {
      Expect.equal (PostComments.emptyText PostComments.All) "No comments yet." "All"
      Expect.equal
        (PostComments.emptyText(PostComments.Only Api.CommentApproval.Rejected))
        "No rejected comments."
        "Rejected"
    }

    test "each row offers only the changes that its approval allows" {
      Expect.equal (PostComments.actions Api.CommentApproval.New) [ Queue.Approve; Queue.Reject; Queue.Delete ] "New"
      Expect.equal (PostComments.actions Api.CommentApproval.Approved) [ Queue.Reject; Queue.Delete ] "Approved"
      Expect.equal (PostComments.actions Api.CommentApproval.Rejected) [ Queue.Approve; Queue.Delete ] "Rejected"
      let _, effect = send (PostComments.Chosen("approved", Queue.Approve)) (loaded ())
      Expect.isNone effect "No approval of an approved comment"
    }

    test "Approve sends the approval, and the answer approves the row and removes it from the moderation queue" {
      let model, _ = start "/posts/first/comments"
      let queued: Api.PostedComment = { PostSlug = "first"; PostTitle = "First"; Comment = waiting }
      let model, _ = Shell.update (Shell.Queue(Queue.Answered(Ok [ queued ]))) model
      let model, _ = send (PostComments.Answered("first", Ok [ waiting; approved; rejected ])) model

      let model, effect = send (PostComments.Chosen("waiting", Queue.Approve)) model
      Expect.equal effect (Some(Shell.Send [ Shell.ModeratePostComment("first", waiting, Queue.Approve) ])) "The approval call"
      let model, _ = send (PostComments.Done("waiting", Queue.Approve, Ok())) model
      Expect.equal (approval "waiting" model) (Some Api.CommentApproval.Approved) "The row is approved"
      Expect.equal (Queue.count model.Queue) (Some 0) "The navigation count goes down"
    }

    test "Approve on a rejected comment approves the row" {
      let model, effect = send (PostComments.Chosen("rejected", Queue.Approve)) (loaded ())
      Expect.equal effect (Some(Shell.Send [ Shell.ModeratePostComment("first", rejected, Queue.Approve) ])) "The approval call"
      let model, _ = send (PostComments.Done("rejected", Queue.Approve, Ok())) model
      Expect.equal (approval "rejected" model) (Some Api.CommentApproval.Approved) "The row is approved"
    }

    test "a failed action keeps the approval and shows the reason" {
      let model, _ = send (PostComments.Chosen("waiting", Queue.Reject)) (loaded ())
      let model, _ = send (PostComments.Done("waiting", Queue.Reject, Error(Api.Failed "Comment 'waiting' not found"))) model
      let row = PostComments.tryFind "waiting" (page model) |> Option.get
      Expect.equal (Queue.failure row.State) (Some "Couldn't reject the comment. Comment 'waiting' not found") "The reason"
      Expect.equal row.Comment.Status Api.CommentApproval.New "The approval stays"
    }

    test "Delete in the confirmation closes it, sends the delete, and the answer removes the row" {
      let model, _ = Shell.update (Shell.Opened(Shell.DeleteDialog "rejected")) (loaded ())
      let chosen = Shell.deleteChosen model
      Expect.equal chosen (Some(Shell.PostComments(PostComments.Chosen("rejected", Queue.Delete)))) "The delete of the page"
      let model, effect = Shell.update chosen.Value model
      Expect.isNone model.Overlay "The confirmation closes"
      Expect.equal effect (Some(Shell.Send [ Shell.ModeratePostComment("first", rejected, Queue.Delete) ])) "The delete call"
      let model, _ = send (PostComments.Done("rejected", Queue.Delete, Ok())) model
      Expect.equal (shown model) [ "waiting"; "approved" ] "The row goes away"
    }

    test "a delete with no confirmation sends nothing" {
      let _, effect = send (PostComments.Chosen("rejected", Queue.Delete)) (loaded ())
      Expect.isNone effect "No call"
    }

    test "comments that did not load show the reason" {
      let model, _ = start "/posts/first/comments"
      let model, _ = send (PostComments.Answered("first", Error(Api.Failed "Post 'first' not found"))) model
      Expect.equal (page model).Comments (PostComments.NotLoaded "Post 'first' not found") "The reason"
    }

    test "a refused token offers another account" {
      let model, _ = start "/posts/first/comments"
      let model, _ = send (PostComments.Answered("first", Error Api.Refused)) model
      Expect.equal model.Session.Status Session.Refused "The banner offers another account"
    }

    replyTests
  ]
