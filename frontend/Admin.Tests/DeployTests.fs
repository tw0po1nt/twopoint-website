module TwoPoint.Admin.Tests.DeployTests

open TwoPoint.Admin

open Expecto

let private noDetails: Api.PostDetails =
  { Author = None
    Category = None
    Tags = []
    Series = None
    Image = None
    ShowOnHomepage = true }

let private document state : Api.PostDocument =
  { Slug = "my-post"
    State = state
    PendingEdit = false
    PublishDate = Some "2026-02-04T13:00:00Z"
    ETag = None
    Title = "My post"
    Excerpt = "About it."
    Body = "Some text.\n"
    Details = noDetails
    Text = ""
    PublishedText = None }

let private started (path: string) =
  let model, _ = Shell.init path
  fst (Shell.update (Shell.Session(Session.Started true)) model)

let private open' (post: Api.PostDocument) =
  Shell.update (Shell.Editor(Editor.Answered("my-post", Ok post))) (started "/posts/my-post")

let private deployMsg msg = Shell.Editor(Editor.DeployMsg("my-post", msg))

let private read state = deployMsg (Deploy.StatusRead(Ok state))

let private editor (model: Shell.Model) =
  match model.Editor with
  | Some(Editor.Loaded document) -> document
  | other -> failtestf "Expected a loaded editor, got %A" other

let private status (model: Shell.Model) = Editor.statusText (editor model)

let private post slug deploy : Api.Post =
  { Slug = slug
    Title = slug
    State = Api.Published
    PendingEdit = false
    PublishDate = None
    Deploy = deploy
    CommentStats = { New = 0; Approved = 0; Rejected = 0 } }

let private listed (posts: Api.Post list) =
  Shell.update (Shell.Posts(Posts.Answered(Ok posts))) (started "/posts")

[<Tests>]
let tests =
  testList "Deploy state" [
    test "the editor reads the deploy state when it loads a published post" {
      let _, effect = open' (document Api.Published)
      Expect.equal effect (Some(Shell.Send [ Shell.LoadPostDeploy "my-post" ])) "The read"
    }

    test "the editor reads the deploy state when it loads an upcoming post" {
      let _, effect = open' (document Api.Upcoming)
      Expect.equal effect (Some(Shell.Send [ Shell.LoadPostDeploy "my-post" ])) "The read"
    }

    test "the editor reads no deploy state when it loads a draft" {
      let _, effect = open' (document Api.Draft)
      Expect.equal effect None "No read"
    }

    test "the poll starts while a change deploys" {
      let model, _ = open' (document Api.Published)
      let model, effect = Shell.update (read Api.DeployState.Deploying) model
      Expect.equal effect (Some(Shell.WaitToPollDeploy "my-post")) "A timer"
      Expect.equal (status model) "Published Feb 4, 2026 · deploying…" "The status"
    }

    test "the poll reads again when the timer ends while the change deploys" {
      let model, _ = open' (document Api.Published)
      let model, _ = Shell.update (read Api.DeployState.Deploying) model
      let _, effect = Shell.update (deployMsg Deploy.PollElapsed) model
      Expect.equal effect (Some(Shell.Send [ Shell.LoadPostDeploy "my-post" ])) "The read"
    }

    test "the poll stops when the change is deployed" {
      let model, _ = open' (document Api.Published)
      let model, _ = Shell.update (read Api.DeployState.Deploying) model
      let model, effect = Shell.update (read Api.DeployState.Deployed) model
      Expect.equal effect None "No timer"
      Expect.equal (status model) "Published Feb 4, 2026 · deployed" "The status"

      let _, effect = Shell.update (deployMsg Deploy.PollElapsed) model
      Expect.equal effect None "A late timer reads nothing"
    }

    test "the poll stops when the deploy failed, and the status offers a retry" {
      let model, _ = open' (document Api.Published)
      let model, effect = Shell.update (read Api.DeployState.DeployFailed) model
      Expect.equal effect None "No timer"
      Expect.equal (status model) "Published Feb 4, 2026 · deploy failed" "The status"
      Expect.isTrue (Editor.canRetryDeploy (editor model)) "Retry"
    }

    test "a deployed post offers no retry" {
      let model, _ = open' (document Api.Published)
      let model, _ = Shell.update (read Api.DeployState.Deployed) model
      Expect.isFalse (Editor.canRetryDeploy (editor model)) "No retry"
    }

    test "a read that the API does not answer keeps the poll" {
      let model, _ = open' (document Api.Published)
      let model, effect = Shell.update (deployMsg (Deploy.StatusRead(Error Api.Unanswered))) model
      Expect.equal effect (Some(Shell.WaitToPollDeploy "my-post")) "A timer"
      Expect.equal (status model) "Published Feb 4, 2026 · deploy state unknown" "The status"
    }

    test "a read that the API refuses ends the poll and the session" {
      let model, _ = open' (document Api.Published)
      let _, effect = Shell.update (deployMsg (Deploy.StatusRead(Error Api.Refused))) model
      Expect.isFalse (effect = Some(Shell.WaitToPollDeploy "my-post")) "No timer"
    }

    test "a read of another post changes nothing" {
      let model, _ = open' (document Api.Published)
      let other, effect = Shell.update (Shell.Editor(Editor.DeployMsg("other", Deploy.StatusRead(Ok Api.DeployState.DeployFailed)))) model
      Expect.equal other model "The same model"
      Expect.equal effect None "No effect"
    }

    test "retry dispatches the rebuild once, and a rebuild that starts reads the state again" {
      let model, _ = open' (document Api.Published)
      let model, _ = Shell.update (read Api.DeployState.DeployFailed) model
      let model, effect = Shell.update (deployMsg Deploy.RetryClicked) model
      Expect.equal effect (Some(Shell.Send [ Shell.RetryPostDeploy "my-post" ])) "The rebuild"

      let model, effect = Shell.update (deployMsg Deploy.RetryClicked) model
      Expect.equal effect None "A second click sends nothing"

      let model, effect = Shell.update (deployMsg (Deploy.Retried(Ok(Api.Queued 12)))) model
      Expect.equal effect (Some(Shell.Send [ Shell.LoadPostDeploy "my-post" ])) "The read"
      Expect.equal (status model) "Published Feb 4, 2026" "The state is unread"
    }

    test "a rebuild that does not start shows the reason and keeps the retry" {
      let model, _ = open' (document Api.Published)
      let model, _ = Shell.update (read Api.DeployState.DeployFailed) model
      let model, _ = Shell.update (deployMsg Deploy.RetryClicked) model
      let model, effect = Shell.update (deployMsg (Deploy.Retried(Error(Api.Failed "The dispatch failed.")))) model
      Expect.equal effect None "No read"
      Expect.equal (Editor.retryFailureText (editor model)) "The dispatch failed." "The reason"
      Expect.isTrue (Editor.canRetryDeploy (editor model)) "Retry"
    }

    test "a publish reads the deploy state of the post" {
      let model, _ = open' { document Api.Draft with ETag = Some "\"v1\"" }
      let model, _ = Shell.update (Shell.Editor Editor.PublishClicked) model
      let model, _ = Shell.update (Shell.Editor(Editor.PublishCheckAnswered("my-post", Publish.Now, Ok { Problems = [] }))) model
      let model, _ = Shell.update (Shell.Editor Editor.PublishConfirmed) model

      let published: Api.PublishedPost =
        { PublishDate = "2026-03-01T09:30:45Z"
          State = Api.Published
          Warnings = [] }

      let model, effect = Shell.update (Shell.Editor(Editor.PublishAnswered("my-post", Ok published))) model
      Expect.equal effect (Some(Shell.Send [ Shell.LoadPostDeploy "my-post" ])) "The read"
      Expect.equal (editor model).Deploy Deploy.init "The deploy state is unread"
    }

    test "the post list polls while a post deploys, and stops when none deploys" {
      let model, effect = listed [ post "a" Api.DeployState.Deployed; post "b" Api.DeployState.Deploying ]
      Expect.equal effect (Some Shell.WaitToPollPosts) "A timer"

      let _, effect = Shell.update (Shell.Posts Posts.PollElapsed) model
      Expect.equal effect (Some(Shell.Send [ Shell.PollPosts ])) "The read"

      let model, effect = Shell.update (Shell.Posts(Posts.Polled(Ok [ post "a" Api.DeployState.Deployed; post "b" Api.DeployState.Deployed ]))) model
      Expect.equal effect None "No timer"

      let _, effect = Shell.update (Shell.Posts Posts.PollElapsed) model
      Expect.equal effect None "A late timer reads nothing"
    }

    test "a failed post in the list offers a retry that rebuilds and reads the list again" {
      let model, _ = listed [ post "a" Api.DeployState.DeployFailed ]
      Expect.isTrue (Posts.canRetry (post "a" Api.DeployState.DeployFailed)) "Retry"

      let model, effect = Shell.update (Shell.Posts Posts.RetryClicked) model
      Expect.equal effect (Some(Shell.Send [ Shell.RetryPostsDeploy ])) "The rebuild"
      Expect.isTrue model.Posts.Retrying "The call is in flight"

      let model, effect = Shell.update (Shell.Posts(Posts.Retried(Ok Api.QueuedWithoutRunNumber))) model
      Expect.equal effect (Some(Shell.Send [ Shell.PollPosts ])) "The read"
      Expect.isFalse model.Posts.Retrying "The call ended"
    }

    test "a draft in the list shows no deploy state" {
      Expect.isFalse (Posts.showsDeploy { post "a" Api.DeployState.Deployed with State = Api.Draft }) "A draft"
      Expect.isTrue (Posts.showsDeploy (post "a" Api.DeployState.Deployed)) "A published post"
    }

    test "a late timer of the list after the author left the page reads nothing" {
      let model, _ = listed [ post "b" Api.DeployState.Deploying ]
      let model, _ = Shell.update (Shell.Router(Router.LinkFollowed "/")) model
      let _, effect = Shell.update (Shell.Posts Posts.PollElapsed) model
      Expect.equal effect None "No read"
    }
  ]
