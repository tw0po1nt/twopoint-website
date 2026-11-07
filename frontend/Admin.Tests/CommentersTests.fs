module TwoPoint.Admin.Tests.CommentersTests

open TwoPoint.Admin

open Expecto

let private commenter id status : Api.Commenter =
  { Id = id
    Email = $"{id}@example.com"
    Name = id
    Status = status
    CreatedDate = "2026-01-02T03:04:05Z" }

let private ada = commenter "ada" Api.New
let private grace = commenter "grace" Api.Trusted
let private alan = commenter "alan" Api.Banned

let private posted id (commenter: Api.Commenter) approval : Api.PostedComment =
  { PostSlug = "first"
    PostTitle = "First"
    Comment =
      { Id = id
        CreatedDate = "2026-02-01T00:00:00Z"
        Writer = Api.Writer.Commenter commenter
        Status = approval
        Content = "A comment"
        ReplyTo = None } }

let private waiting = posted "waiting" ada Api.CommentApproval.New
let private approved = posted "approved" ada Api.CommentApproval.Approved

let private start (path: string) =
  let model, _ = Shell.init path
  Shell.update (Shell.Session(Session.Started true)) model

let private listed () =
  let model, _ = start "/commenters"
  let model, _ = Shell.update (Shell.Commenters(Commenters.Answered(Ok [ ada; grace; alan ]))) model
  model

let private shown (model: Shell.Model) =
  Commenters.shown model.Commenters |> List.map _.Id

let private send msg model = Shell.update (Shell.Commenter msg) model

let private opened () =
  let model, _ = start "/commenters/ada"
  let model, _ = send (Commenter.Answered("ada", Ok { Commenter = ada; Comments = [ waiting; approved ] })) model
  model

let private page (model: Shell.Model) = model.Commenter |> Option.get

let private loaded (model: Shell.Model) =
  match (page model).Page with
  | Commenter.Loaded(page, state) -> page, state
  | other -> failtestf "Expected a loaded page, got %A" other

let private listTests =
  testList "Commenters" [
    test "a start on the commenter list loads the commenters with the first API calls" {
      let model, effect = start "/commenters"
      Expect.equal effect (Some(Shell.Send [ Shell.LoadQueue; Shell.LoadCommenters ])) "The commenters"
      Expect.equal model.Commenters.Commenters Commenters.Waiting "The list waits"
    }

    test "the filter shows all commenters, or only the new, the trusted, or the banned commenters" {
      let model = listed ()
      Expect.equal model.Commenters.Filter Commenters.All "All at first"
      Expect.equal (shown model) [ "ada"; "grace"; "alan" ] "All"

      for status, expected in [ Api.New, [ "ada" ]; Api.Trusted, [ "grace" ]; Api.Banned, [ "alan" ] ] do
        let model, effect = Shell.update (Shell.Commenters(Commenters.Filtered(Commenters.Only status))) model
        Expect.isNone effect "The filter calls no API"
        Expect.equal (shown model) expected $"Only {status}"

      let model, _ = Shell.update (Shell.Commenters(Commenters.Filtered(Commenters.Only Api.Banned))) model
      let model, _ = Shell.update (Shell.Commenters(Commenters.Filtered Commenters.All)) model
      Expect.equal (shown model) [ "ada"; "grace"; "alan" ] "All again"
    }

    test "the filter stays after a visit to a commenter page, and the list loads again" {
      let model, _ = Shell.update (Shell.Commenters(Commenters.Filtered(Commenters.Only Api.Banned))) (listed ())
      let model, _ = Shell.update (Shell.Router(Router.LinkFollowed "/commenters/alan")) model
      let model, effect = Shell.update (Shell.Router(Router.UrlChanged "/commenters")) model
      Expect.equal effect (Some(Shell.Send [ Shell.LoadCommenters ])) "The commenters again"
      Expect.equal model.Commenters.Filter (Commenters.Only Api.Banned) "The banned commenters"
    }

    test "a filter with no commenter has its own empty text" {
      Expect.equal (Commenters.emptyText Commenters.All) "No commenters yet." "All"
      Expect.equal (Commenters.emptyText(Commenters.Only Api.Banned)) "No banned commenters." "Banned"
    }

    test "the verified date is the day of the date" {
      Expect.equal (Commenters.verifiedText ada) "Verified: 2026-01-02" "The day"
    }

    test "commenters that did not load show the reason" {
      let model, _ = start "/commenters"
      let model, _ = Shell.update (Shell.Commenters(Commenters.Answered(Error(Api.Failed "No table")))) model
      Expect.equal model.Commenters.Commenters (Commenters.NotLoaded "No table") "The reason"
    }

    test "a refused token offers another account" {
      let model, _ = start "/commenters"
      let model, _ = Shell.update (Shell.Commenters(Commenters.Answered(Error Api.Refused))) model
      Expect.equal model.Session.Status Session.Refused "The banner offers another account"
    }
  ]

let private pageTests =
  testList "Commenter" [
    test "a link to a commenter page pushes its path and loads the commenter" {
      let model, _ = start "/comments"
      let model, effect = Shell.update (Shell.Router(Router.LinkFollowed "/commenters/ada")) model
      Expect.equal effect (Some(Shell.Navigate(Router.PushUrl "/commenters/ada", [ Shell.LoadCommenter "ada" ]))) "The commenter"
      Expect.isTrue (Router.isCurrent Commenters model.Router.Route) "The commenters are the current page in the navigation"
    }

    test "an answer for another commenter changes nothing" {
      let model, _ = start "/commenters/grace"
      let next, _ = send (Commenter.Answered("ada", Ok { Commenter = ada; Comments = [] })) model
      Expect.equal next model "The page still waits for its own commenter"
    }

    test "each status offers only the changes that the backend allows" {
      Expect.equal (Commenter.actions Api.New) [ Commenter.Trust; Commenter.Ban; Commenter.Delete ] "New"
      Expect.equal (Commenter.actions Api.Trusted) [ Commenter.Untrust; Commenter.Ban; Commenter.Delete ] "Trusted"
      Expect.equal (Commenter.actions Api.Banned) [ Commenter.Unban; Commenter.Delete ] "Banned"
      let _, effect = send (Commenter.Chosen Commenter.Unban) (opened ())
      Expect.isNone effect "No unban of a new commenter"
    }

    test "Trust sends the trust, and the answer trusts the commenter and loads the moderation queue again" {
      let model, effect = send (Commenter.Chosen Commenter.Trust) (opened ())
      Expect.equal effect (Some(Shell.Send [ Shell.ChangeCommenter("ada", Commenter.Trust) ])) "The trust call"
      Expect.isTrue (Progress.isSending (snd (loaded model))) "The buttons are off"
      let model, effect = send (Commenter.Done("ada", Commenter.Trust, Ok())) model
      Expect.equal effect (Some(Shell.Send [ Shell.LoadQueue ])) "The queue again"
      Expect.equal (fst (loaded model)).Commenter.Status Api.Trusted "Trusted"
    }

    test "Ban bans the commenter, rejects each new comment, and keeps each approved comment" {
      let model, _ = send (Commenter.Chosen Commenter.Ban) (opened ())
      let model, _ = send (Commenter.Done("ada", Commenter.Ban, Ok())) model
      let page, _ = loaded model
      Expect.equal page.Commenter.Status Api.Banned "Banned"
      Expect.equal
        (page.Comments |> List.map (fun comment -> comment.Comment.Id, comment.Comment.Status))
        [ "waiting", Api.CommentApproval.Rejected; "approved", Api.CommentApproval.Approved ]
        "Only the new comment is rejected"
    }

    test "Unban makes the commenter new, and the rejected comments stay rejected" {
      let model, _ = send (Commenter.Chosen Commenter.Ban) (opened ())
      let model, _ = send (Commenter.Done("ada", Commenter.Ban, Ok())) model
      let model, effect = send (Commenter.Chosen Commenter.Unban) model
      Expect.equal effect (Some(Shell.Send [ Shell.ChangeCommenter("ada", Commenter.Unban) ])) "The unban call"
      let model, _ = send (Commenter.Done("ada", Commenter.Unban, Ok())) model
      let page, _ = loaded model
      Expect.equal page.Commenter.Status Api.New "New"
      Expect.equal (page.Comments |> List.map _.Comment.Status) [ Api.CommentApproval.Rejected; Api.CommentApproval.Approved ] "Rejected stays"
    }

    test "the status after each action" {
      Expect.equal (Commenter.statusAfter Commenter.Trust) (Some Api.Trusted) "Trust"
      Expect.equal (Commenter.statusAfter Commenter.Untrust) (Some Api.New) "Untrust"
      Expect.equal (Commenter.statusAfter Commenter.Ban) (Some Api.Banned) "Ban"
      Expect.equal (Commenter.statusAfter Commenter.Unban) (Some Api.New) "Unban"
      Expect.equal (Commenter.statusAfter Commenter.Delete) None "Delete"
    }

    test "a failed action keeps the status and shows the reason" {
      let model, _ = send (Commenter.Chosen Commenter.Ban) (opened ())
      let model, effect = send (Commenter.Done("ada", Commenter.Ban, Error(Api.Failed "Commenter 'ada' not found"))) model
      Expect.isNone effect "No call"
      let page, state = loaded model
      Expect.equal (Commenter.failure state) (Some "Couldn't ban the commenter. Commenter 'ada' not found") "The reason"
      Expect.equal page.Commenter.Status Api.New "The status stays"
    }

    test "a delete with no confirmation sends nothing" {
      let _, effect = send (Commenter.Chosen Commenter.Delete) (opened ())
      Expect.isNone effect "No call"
    }

    test "Delete in the confirmation closes it, and the answer opens the commenter list and loads the queue again" {
      let model, _ = Shell.update (Shell.Opened Shell.DeleteCommenterDialog) (opened ())
      let chosen = Shell.deleteCommenterChosen model
      Expect.equal chosen (Some(Shell.Commenter(Commenter.Chosen Commenter.Delete))) "The delete of the page"
      let model, effect = Shell.update chosen.Value model
      Expect.isNone model.Overlay "The confirmation closes"
      Expect.equal effect (Some(Shell.Send [ Shell.ChangeCommenter("ada", Commenter.Delete) ])) "The delete call"
      let model, effect = send (Commenter.Done("ada", Commenter.Delete, Ok())) model
      Expect.equal model.Router.Route Commenters "The commenter list"
      Expect.equal
        effect
        (Some(Shell.Navigate(Router.PushUrl "/commenters", [ Shell.LoadCommenters; Shell.LoadQueue ])))
        "The list and the queue again"
    }

    test "a delete answer after the author left the page keeps the page" {
      let model, _ = Shell.update (Shell.Opened Shell.DeleteCommenterDialog) (opened ())
      let model, _ = Shell.update (Shell.deleteCommenterChosen model).Value model
      let model, _ = Shell.update (Shell.Router(Router.LinkFollowed "/comments")) model
      let model, effect = send (Commenter.Done("ada", Commenter.Delete, Ok())) model
      Expect.equal model.Router.Route ModerationQueue "The queue stays"
      Expect.equal effect (Some(Shell.Send [ Shell.LoadQueue ])) "The queue again"
    }

    test "a refused token offers another account" {
      let model, _ = start "/commenters/ada"
      let model, _ = send (Commenter.Answered("ada", Error Api.Refused)) model
      Expect.equal model.Session.Status Session.Refused "The banner offers another account"
    }
  ]

[<Tests>]
let tests = testList "Commenters" [ listTests; pageTests ]
