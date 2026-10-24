module TwoPoint.Admin.Tests.QueueTests

open TwoPoint.Admin

open Expecto

let private queued id status : Api.QueuedComment =
  { PostSlug = "first-post"
    PostTitle = "First post"
    Comment =
      { Id = id
        CreatedDate = "2026-02-01T00:00:00Z"
        Commenter =
          { Email = "ada@example.com"
            Name = "Ada"
            Status = status }
        Status = "new"
        Content = "A comment" } }

let private first = queued "first" "new"
let private second = queued "second" "trusted"

let private send msg model = Shell.update (Shell.Queue msg) model

let private loaded () =
  let model, _ = Shell.init "/comments"
  let model, _ = Shell.update (Shell.Session(Session.Started true)) model
  let model, _ = send (Queue.Answered(Ok [ first; second ])) model
  model

[<Tests>]
let tests =
  testList "Queue" [
    test "the navigation count is the number of comments in the queue, after the queue loads" {
      let model, _ = Shell.init "/comments"
      Expect.isNone (Queue.count model.Queue) "No count before the answer"
      Expect.equal (Queue.count (loaded ()).Queue) (Some 2) "Two new comments"
    }

    test "Approve sends the approval, and the answer removes the row and lowers the navigation count" {
      let model, effect = send (Queue.Chosen("first", Queue.Approve)) (loaded ())
      Expect.equal effect (Some(Shell.Send [ Shell.Moderate(first, Queue.Approve) ])) "The approval call"
      Expect.isTrue (Queue.tryFind "first" model.Queue |> Option.exists Queue.isSending) "The buttons are off"
      Expect.equal (Queue.count model.Queue) (Some 2) "The count stays until the answer"

      let model, effect = send (Queue.Done("first", Queue.Approve, Ok())) model
      Expect.isNone effect "No more calls"
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
        (row |> Option.bind Queue.reason)
        (Some "The approval did not complete. Comment is approved and cannot become approved")
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

    test "the commenter status of a row" {
      let model = loaded ()
      let status id = Queue.tryFind id model.Queue |> Option.map Queue.commenterStatus
      Expect.equal (status "first") (Some Queue.New) "A new commenter"
      Expect.equal (status "second") (Some Queue.Trusted) "A trusted commenter"
    }

    test "a queue that did not load shows the reason" {
      let model, _ = Shell.init "/comments"
      let model, _ = send (Queue.Answered(Error Api.Unanswered)) model
      Expect.equal model.Queue (Queue.NotLoaded "The API did not answer.") "The reason"
    }

    test "a refused token offers another account" {
      let model, _ = send (Queue.Chosen("first", Queue.Approve)) (loaded ())
      let model, _ = send (Queue.Done("first", Queue.Approve, Error Api.Refused)) model
      Expect.equal model.Session.Status Session.Refused "The banner offers another account"
    }
  ]
