module TwoPoint.Admin.Tests.SendTests

open TwoPoint.Admin

open Expecto

let private noDetails: Api.PostDetails =
  { Author = None
    Category = None
    Tags = []
    Series = None
    Image = None
    ShowOnHomepage = true }

let private post state publishDate eTag : Api.PostDocument =
  { Slug = "my-post"
    State = state
    PendingEdit = false
    PublishDate = publishDate
    ETag = eTag
    Title = "My post"
    Excerpt = "About it."
    Body = "Some text.\n"
    Details = noDetails
    DeliveryStarted = None
    Text = ""
    Published =
      Some
        { Title = "My post"
          Excerpt = "About it."
          Body = "Some text.\n"
          Details = noDetails } }

let private live = post Api.Published (Some "2026-02-04T13:00:00Z") None

let private opened (post: Api.PostDocument) =
  let model, _ = Shell.init "/posts/my-post"
  let model, _ = Shell.update (Shell.Session(Session.Started true)) model
  fst (Shell.update (Shell.Editor(Editor.Answered("my-post", Ok post))) model)

let private send msg (model: Shell.Model) = Shell.update (Shell.Editor msg) model

let private withDeploy (state: Api.DeployState) (model: Shell.Model) =
  fst (send (Editor.DeployMsg("my-post", Deploy.StatusRead(Ok state))) model)

let private deployed () = opened live |> withDeploy Api.DeployState.Deployed

let private document' (model: Shell.Model) =
  match model.Editor with
  | Some(Editor.Loaded document) -> document
  | other -> failtestf "Expected a loaded editor, got %A" other

let private availabilityOf (model: Shell.Model) =
  (Editor.menu (document' model) |> List.find (fun item -> item.Action = Editor.SendToSubscribers)).Availability

let private started: Api.StartedDelivery = { Queued = 3; StartedDate = "2026-10-30T12:00:00Z" }

let private reasonOf (model: Shell.Model) =
  match availabilityOf model with
  | Editor.Unavailable reason -> reason
  | Editor.Available -> ""

let private typing (intro: string) =
  let model, _ = send Editor.SendRequested (deployed ())
  fst (send (Editor.IntroTyped intro) model)

[<Tests>]
let menuTests =
  testList "Send to subscribers menu" [
    test "a published post with its latest change deployed can be sent, and the action sends its message" {
      Expect.equal (availabilityOf (deployed ())) Editor.Available "Available"
      Expect.equal (Editor.menuMsg Editor.SendToSubscribers) (Some Editor.SendRequested) "The message"
    }

    test "a draft and an upcoming post give the reason" {
      Expect.equal (reasonOf (opened (post Api.Draft None (Some "\"v1\"")))) "Publish the post first." "Draft"

      let upcoming = opened (post Api.Upcoming (Some "2099-12-31T00:00:00Z") None) |> withDeploy Api.DeployState.Deployed

      Expect.equal
        (reasonOf upcoming)
        "The post is not published yet. Start the delivery after its publish date."
        "Upcoming"
    }

    test "a published post waits for the deploy of its latest change, and each deploy state gives its reason" {
      Expect.equal (reasonOf (opened live)) "The admin reads the deploy state of the post." "Unread"

      Expect.equal
        (reasonOf (opened live |> withDeploy Api.DeployState.Deploying))
        "The latest change to the post deploys. Start the delivery after the deploy."
        "Deploying"

      Expect.equal
        (reasonOf (opened live |> withDeploy Api.DeployState.DeployFailed))
        "The deploy of the latest change failed. Rebuild the site, and then start the delivery."
        "Failed"

      let unreadable, _ = send (Editor.DeployMsg("my-post", Deploy.StatusRead(Error Api.Unanswered))) (opened live)

      Expect.equal
        (reasonOf unreadable)
        "The deploy state of the post is not known. Reload the page."
        "Unreadable"
    }

    test "a post with a delivery start gives the date of the start" {
      let started = opened { live with DeliveryStarted = Some "2026-10-30T12:00:00Z" } |> withDeploy Api.DeployState.Deployed
      Expect.equal (reasonOf started) "The delivery of the post started on 2026-10-30." "Started"
    }

    test "a request while the action is not available opens no dialog" {
      let model, effect = send Editor.SendRequested (opened live |> withDeploy Api.DeployState.Deploying)
      Expect.isNone effect "No call"
      Expect.equal (document' model).SendToSubscribers Typed.Closed "No dialog"
    }
  ]

[<Tests>]
let sendTests =
  testList "Send to subscribers" [
    test "the request opens the dialog with an empty intro" {
      let model, effect = send Editor.SendRequested (deployed ())
      Expect.isNone effect "No call yet"
      Expect.equal (document' model).SendToSubscribers (Typed.Typing "") "The dialog"
    }

    test "the send starts the delivery with the typed intro" {
      let model, effect = send Editor.SendConfirmed (typing "A note.")
      Expect.equal (document' model).SendToSubscribers (Typed.Sending "A note.") "Sending"

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.StartDelivery { Slug = "my-post"; Intro = "A note." } ]))
        "The delivery call"
    }

    test "a started delivery closes the dialog and shows the count until the author dismisses it" {
      let sending, _ = send Editor.SendConfirmed (typing "")
      let model, effect = send (Editor.SendAnswered("my-post", Ok started)) sending
      Expect.isNone effect "No call"
      Expect.equal (document' model).SendToSubscribers Typed.Closed "Closed"
      Expect.equal (document' model).QueuedCount (Some 3) "The count"

      let dismissed, _ = send Editor.SentNoticeDismissed model
      Expect.equal (document' dismissed).QueuedCount None "Dismissed"
    }

    test "a started delivery makes the action unavailable" {
      let sending, _ = send Editor.SendConfirmed (typing "")
      let model, _ = send (Editor.SendAnswered("my-post", Ok started)) sending
      Expect.equal (reasonOf model) "The delivery of the post started on 2026-10-30." "Started"

      let requested, effect = send Editor.SendRequested model
      Expect.isNone effect "No call"
      Expect.equal (document' requested).SendToSubscribers Typed.Closed "No dialog"
    }

    test "a refused delivery keeps the intro and shows the reason, and a second send tries again" {
      let sending, _ = send Editor.SendConfirmed (typing "A note.")
      let refused = Api.Failed "The latest change to post 'my-post' is not deployed yet"
      let model, _ = send (Editor.SendAnswered("my-post", Error refused)) sending

      Expect.equal
        (document' model).SendToSubscribers
        (Typed.Failed("A note.", "The latest change to post 'my-post' is not deployed yet"))
        "The reason"

      let _, effect = send Editor.SendConfirmed model

      Expect.equal
        effect
        (Some(Shell.Send [ Shell.StartDelivery { Slug = "my-post"; Intro = "A note." } ]))
        "The second try"
    }

    test "the dialog stays open while the delivery starts, and closes after it" {
      let sending, _ = send Editor.SendConfirmed (typing "")
      let kept, _ = send Editor.SendClosed sending
      Expect.equal (document' kept).SendToSubscribers (Typed.Sending "") "Still sending"

      let closed, _ = send Editor.SendClosed (typing "A note.")
      Expect.equal (document' closed).SendToSubscribers Typed.Closed "Closed"
    }

    test "an answer for another post changes nothing" {
      let sending, _ = send Editor.SendConfirmed (typing "")
      let model, _ = send (Editor.SendAnswered("other-post", Ok started)) sending
      Expect.equal (document' model).SendToSubscribers (Typed.Sending "") "Still sending"
    }

    test "the notice counts the active subscriptions" {
      Expect.equal (Editor.queuedText 0) "No subscription is active, so the admin sent no email." "None"
      Expect.equal (Editor.queuedText 1) "The admin queued the email for 1 active subscription." "One"
      Expect.equal (Editor.queuedText 4) "The admin queued the email for 4 active subscriptions." "Many"
    }
  ]
