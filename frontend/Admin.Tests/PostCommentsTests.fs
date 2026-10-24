module TwoPoint.Admin.Tests.PostCommentsTests

open TwoPoint.Admin

open Expecto

let private comment id approval : Api.Comment =
  { Id = id
    CreatedDate = "2026-02-01T00:00:00Z"
    Commenter =
      { Id = "ada-id"
        Email = "ada@example.com"
        Name = "Ada"
        Status = Api.New }
    Status = approval
    Content = "A comment" }

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

[<Tests>]
let tests =
  testList "PostComments" [
    test "a start on the page of a post loads its comments with the first API calls" {
      let model, effect = start "/posts/first/comments"
      Expect.equal
        effect
        (Some(Shell.Send [ Shell.LoadDashboard; Shell.LoadQueue; Shell.LoadPostComments "first" ]))
        "The comments of the post"
      Expect.equal (page model).Comments PostComments.Waiting "The page waits"
    }

    test "a link to the page of a post pushes its path and loads its comments" {
      let model, _ = start "/"
      let model, effect = Shell.update (Shell.Router(Router.LinkFollowed "/posts/first/comments")) model
      Expect.equal
        effect
        (Some(Shell.Navigate(Router.PushUrl "/posts/first/comments", [ Shell.LoadPostComments "first" ])))
        "The URL and the comments"
      Expect.equal (Router.title model.Router.Route) "Comments on first" "The title names the post"
      Expect.isTrue (Router.isCurrent Posts model.Router.Route) "The posts are the current page in the navigation"
    }

    test "the back button to the page of a post loads its comments again" {
      let model = loaded ()
      let model, _ = Shell.update (Shell.Router(Router.UrlChanged "/comments")) model
      let model, effect = Shell.update (Shell.Router(Router.UrlChanged "/posts/first/comments")) model
      Expect.equal effect (Some(Shell.Send [ Shell.LoadPostComments "first" ])) "The comments again"
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
      Expect.equal (PostComments.emptyText PostComments.All) "This post has no comments." "All"
      Expect.equal
        (PostComments.emptyText(PostComments.Only Api.CommentApproval.Rejected))
        "This post has no rejected comments."
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
      let queued: Api.QueuedComment = { PostSlug = "first"; PostTitle = "First"; Comment = waiting }
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
      Expect.equal (Queue.failure row.State) (Some "The rejection did not complete. Comment 'waiting' not found") "The reason"
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
  ]
