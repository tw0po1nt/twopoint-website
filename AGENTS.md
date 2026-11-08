# AGENTS.md

Instructions for agents in this repo.

This repo contains the code for twopoint.dev, a technical blog. `frontend/` is an F# static site
generator with F# widgets that Fable compiles to JavaScript
(`docs/spec/124-build-twopoint-dev-with-fsharp.md`). `backend/` is an F# Azure Functions app for
newsletter subscriptions, notifications, and posts. `frontend/Render/` is the library that renders
and checks a post. The generator and the backend reference it.

Each post is a Markdown blob in the `published` container of the post store, a private Azure Blob
storage account. Each series is a YAML record in the `series` container. A post refers to its
series with `series: <slug>`. A copy of the 10 posts and the 2 series records at the move out of
Git is the test fixture in `frontend/Generator.Tests/posts/`. `dotnet test` reads the fixture and
never reads the post store.

`frontend/Widgets/` and `frontend/Admin/` each pin a Node major version in `.node-version`. On an
older Node, `engines` in `package.json` and `engine-strict` in `.npmrc` stop `npm ci`. The scripts
`prebuild`, `prewatch`, and `pretest` run `frontend/check-node.mjs`, which stops `npm run build`,
`npm run watch`, and `npm test`. Run `nvm use` in the package to select the pinned Node. A change
of the pin needs the same change to `engines.node`, or the check fails.

To build the site, first run `npm ci` and `npm run build` in `frontend/Widgets/`. That build
compiles the widgets with Fable and writes `frontend/Widgets/dist/`. `npm test` in
`frontend/Widgets/` then runs each widget entry of `dist/` in happy-dom. It also checks the Node
pin of each Fable package. Then run
`dotnet run --project frontend/Generator -- pull --account <name>`. The command downloads each blob
in `published` and `series` into `frontend/content/posts/`, which Git ignores. It uses
`DefaultAzureCredential`, so on the local stage it uses your `az login`. The account name is in the
repo variable `POST_STORE_ACCOUNT`. Then run
`dotnet run --project frontend/Generator -- build --stage <local|staging|production>`. If
`frontend/content/posts/` contains no post, the build fails and tells you to run `pull`. The command
writes the site to `frontend/dist/website/`, the newsletters to `frontend/dist/newsletter/`, and
the stage marker to `frontend/dist/stage`. The stage marker contains the stage name of the build.
After a production build, `dotnet run --project frontend/Generator -- upload --container <uri>`
sends each newsletter to Azure Blob. It then deletes each `posts/<slug>/index` blob that has no post
in the build. The command stops if the stage marker is not `production`.

Before the admin build, run
`dotnet run --project frontend/Generator -- preview-styles --out frontend/Admin/dist/preview-styles`.
The command writes the site CSS and its fonts. The preview frame of the editor links them, so
without them the preview has no style. The backend serves the preview at
`POST internal/posts/{slug}/preview`. It reads the `series` and `published` containers.

`frontend/Admin/` is the admin, a separate Fable app that references the Widgets project for shared
code. The site bundle gets no admin code. In `frontend/Admin/`, `npm run build` builds the release
mode and `npm run watch` builds the dev mode at each change. `config.json` contains the public
settings of each mode, with the public VAPID key. The build copies `public/` to `dist/`: the push
worker `sw.js`, `manifest.webmanifest`, and the icons. A build with no VAPID key cannot turn on
notifications. `scripts/setup-web-push.sh` makes the key pair for the deployed backend and writes
the public key to `config.json`. `npm test` tests the last build in happy-dom, with a stub push that has a test key.
The admin dev host in
`frontend/Admin.Dev/` serves the build on `http://localhost:4322` with the rules of
`staticwebapp.config.json`. A SageFs session over `frontend/Admin.Tests/` starts the host and the
watch. In the `staging` repo, `.github/workflows/backend-staging.yml` deploys the admin to Azure
Static Web Apps at `https://admin.twopoint.dev` after the backend. `scripts/setup-admin-host.sh`
sets up that host.

"Rebuild site" on the dashboard sends `workflow_dispatch` to `site.yml` in the two site repos
through a GitHub App. Key Vault keeps the private key of the app and signs each app JWT.
`scripts/setup-site-dispatch.sh` sets up the app. The local backend logs each dispatch and sends
nothing.

The app subscribes to `workflow_run`. The public function `Github-Webhook-Receive`
(`POST /api/webhooks/github`) checks `X-Hub-Signature-256` with the webhook secret and writes each
run of `site.yml` to the table `siteRuns`. The secret is the Key Vault secret that the setting
`Deploy:GitHubApp:WebhookSecretName` names. A bad or missing signature gets `401` and writes
nothing. Each action that dispatches the site stores its change in the table `postChanges` with
`PostChange.ofDispatch` and `PostChanges.RecordChange`. `GET internal/posts` and
`GET internal/posts/{slug}/deploy` give the deploy state of a post. The rule is the pure function
`DeployRule.state`. The local backend logs each dispatch, so each change there is deployed.
A failed production run sends a Web Push to each registered device, and the notification opens
the dashboard. A re-run keeps the run number, and the failure of each attempt sends a Web Push.
`GET internal/deploys` gives the deploy state of the site and the latest run of each
repo. The dashboard shows a failed production run with a link to the run, and a failed staging run
as a warning.

`Admin-Posts-Publish` publishes a draft now or on a future date. It sets the publish date in the
working text and runs the check of the build, with the card image, so an error refuses the publish.
It then writes the `revisions` blob, the `published` blob, and the post-table row, deletes the
`working` blob, and dispatches `site.yml` as "Rebuild site" does. A step after the `published` write
that fails gives a warning, and the post stays published. `Admin-Posts-CheckPublish` gives the
problems for the dialog before the author confirms.

The removal check runs before a published post leaves the site, by a delete or an unpublish. The
delete dialog and the unpublish dialog run it one time when they open, through
`Admin-Posts-CheckRemoval` (`POST internal/posts/{slug}/removal/check`). The rule is the pure
function `RemovalCheck.Logic.outcome`. A publish and remove sends the slug and the working ETag of
each pending edit to publish. `Admin-Posts-Delete` and `Admin-Posts-Unpublish` run the check again,
and a different list or ETag gets `409` and writes nothing. The action publishes each pending edit
with the publish steps of `PublishChanges` and no dispatch. The delete then deletes the post. The
unpublish then writes the draft text, deletes the `published` blob, and deletes the post row. The
action then dispatches `site.yml` one time. Each published edit and the post record their change
with that dispatch. A failed step after the first published edit keeps the post, and the dispatch
still runs for the published edits.

The Series page lists, makes, and edits each series record. `GET internal/series` gives each record
with its posts. `PUT internal/series/{slug}` saves a record and dispatches `site.yml` as "Rebuild
site" does, and the page shows the deploy state of the site. A series save stores no change in
`postChanges`, because a change in that table is for one post. The slug is locked while a post, a
draft, or a pending edit refers to the series. `DELETE internal/series/{slug}` deletes only a series
that no post, draft, or pending edit refers to. The series picker of the Details sheet ends with
"New series…". It opens the form of the Series page (`SeriesForm`, `SeriesFormView`) in the sheet,
saves through the same endpoint, and selects the new series.

"Send to subscribers" in the "•••" menu of the editor starts a new-post delivery, with an optional
intro, through `Admin-Posts-Deliveries-Start` (`POST internal/posts/{slug}/deliveries`). The
backend refuses with `409` until the publish date is in the past and the latest change to the post
is deployed. The rule is the pure function `DeliveryGate.check`. Each start inserts the delivery
start of the post in the table `deliveryStarts` before it queues, so the backend refuses a second
start with `409`. A failed queue deletes the delivery start. The welcome uses the newest post that
passes the same check. The migrated mark is the blob metadata `migrated=true` on a `published`
blob, and each write of a `published` blob keeps it. The filter "Pending deliveries" of the post
list shows each published post that passes the check, with no delivery start and no migrated mark.

`GET internal/dashboard` gives the counts of the dashboard: the drafts, the pending edits, the
deliveries to start, the active subscriptions, and the subscriptions made and canceled in the last
30 days. The backend calculates them at each request with `GetDashboard`, from the same post list
as `GET internal/posts`. The admin reads them at each visit to the dashboard. The moderation queue
count comes from the queue that the admin loads at the start. Each count opens its page with its
filter. The filter of the post list and of the Subscriptions page is part of the route and of the
URL, for example `/posts?filter=drafts`. "All" has no query string.

`Admin-Posts-Comments-Reply` (`POST internal/posts/{slug}/comments/{commentId}/replies`) writes a
reply from the author. The rule is the pure function `AuthorReplyRule.place`. A reply to a rejected
comment gets `409`. A reply on a reply row goes to the top-level comment of the thread. When the
comment is new, the action approves it before the reply, and also its top-level comment if that
comment is new. The reply is approved, has no commenter, and sends no Web Push. The queue and the
comments of a post show the reply form under the row.

The queue, the comments of a post, and the commenter page give the top-level comment of each reply,
with its ID, writer, approval, HTML, and a plain-text excerpt of 200 characters. The rule is the
pure function `AdminComment.ofComments`. A reply row in the queue and on the commenter page shows
"Reply to <name>" and the excerpt, and the excerpt opens the HTML. The comments of a post show
each thread, with the replies indented under the top-level comment. Each comment count includes
the replies.

Each top-level comment in these answers gives `getsReplyEmails`. The flag is true when the
commenter gets reply emails now: a consent, no stop, and no ban. The rule is the pure function
`ReplyEmailRule.getsReplyEmails`. The top-level comment of a reply gives the flag too. When the
flag of the top-level comment is true, the author reply form shows "<name> gets an email with
this reply."

`Admin-Posts-Comments-Delete` and `Admin-Commenters-Delete` follow the pure function
`CommentDeleteRule`. A delete of a top-level comment removes its thread, also a reply from the
author. A delete of a commenter removes each comment of the commenter and each thread that a comment
of the commenter starts. A reply of the commenter in another thread goes alone. The replies go
before the top-level comment, so a second try after a failure finds each thread that stays. After
the top-level comment goes, each delete reads its replies again and deletes each reply that a
reader posts during the delete. A second try after a failure does not find that reply. Each admin
comment gives `replyCount`, and the commenter page gives `otherReplies`. The confirm dialog of each
delete shows that number.

The page form of the Comments widget has the checkbox "email me when someone replies". The reply
form has no checkbox. A checked `replyEmails` on `POST blog/posts/{slug}/comments` stores the
reply-email consent on the top-level comment, with its date and a stop token. The backend also
writes a row from the token to the post slug and the comment ID in the table `replyEmailStops`.
A request with `replyTo` and `replyEmails` gets `400`. A comment with no consent sends no reply
email.

The stop page `/comments/stop/?token=<token>` has the `stop-reply-emails` widget. The widget reads
the state of the token with `GET blog/reply-emails/{token}`: `active` with the post title,
`stopped`, or `unknown`. A deleted comment or a token that is not a GUID is `unknown`. Only the
button `[stop reply emails]` sends `POST blog/reply-emails/{token}/stop`, so a mail scanner that
opens the link stops nothing. The stop writes the stop date on the consent of that one comment. A
second stop answers `stopped` and writes nothing. The rule is the pure module
`ReplyEmailStopRule`. The widget build names each entry with the kebab case of its file name, so
`Entries/StopReplyEmails.fs` is the entry `stop-reply-emails`.

`Blog-Posts-PostComment` puts each new comment on the Service Bus queue `new_comment`. The function
`Queues-ProcessNewCommentMessage` sends a Web Push to each registered device. The worker `sw.js`
builds the notification and sets the app badge. Run `scripts/setup-new-comment-queue.sh` before a
deploy to create the queue.

Each first approval of a reply puts the slug and the reply ID on the Service Bus queue
`reply_email`: a reply that a trusted commenter or the author posts, and a change of a reply from
new to approved. A change from rejected to approved puts nothing on the queue. The rule is the pure
function `ReplyEmailRule.isFirstApproval`. The function `Queues-ProcessReplyEmailMessage` applies
the gate `ReplyEmailRule.check` again, also for a ban of the recipient, and sends the reply email
through ACS. The pure function `ReplyEmailContent.compose` makes the subject, the HTML, and the
plain text. The function writes `ReplyEmailSentDate` on the reply after ACS accepts the email, and
it skips a reply that has that date, so a retry sends no second email. A message that fails each
retry goes to the dead-letter queue. The setting `ReplyEmail:SiteUri` is the root URL of the site
that each email links to. On the local stage it is `http://localhost:4321/`. Run
`scripts/setup-reply-email-queue.sh` before a deploy. It creates the queue and sets
`ReplyEmail:SiteUri` on the Function App.

The backend reads the post store for the post list. The setting `Azure:PostStoreUri` names the
account. On the local stage it is the Azurite URL. `scripts/setup-post-store-access.sh` gives the
backend identity read and write on the four containers and sets the setting on the Function App.

The backend writes each uploaded image and PDF to the `assets` container of the account `twopointwebsite`.
The setting `Azure:AssetsUri` names the account. On the local stage it is the Azurite URL.
`scripts/setup-assets-access.sh` gives the backend identity write access to `assets` only and sets
the setting on the Function App.

`.github/workflows/site.yml` builds the site and deploys `frontend/dist/website/` to GitHub Pages
on each push to `main` and on each manual run. The build action signs in to Azure as the build
identity through OIDC and runs `pull` before `build`. The repo name sets the stage: the `prod`
repo is `production`, and the `staging` repo is `staging`. On `production`, the workflow uploads the newsletters before the
deploy.

## Setup

Run `scripts/bootstrap.sh` after a fresh clone. It installs each skill in `skills-lock.json` into
`.claude/skills/` and `.agents/skills/`. Git ignores both directories.

## Repos and branches

This clone has two remotes. Feature PRs go to `staging` (`tw0po1nt/twopoint-website-staging`).
Release PRs go to `prod` (`tw0po1nt/twopoint-website`). The default branch of each repo is `main`.

## Gate

`scripts/verify.sh` is the gate. It runs the prose check, `dotnet build TwoPoint.slnx`, and
`dotnet test TwoPoint.slnx`. Its last line is `verify: green` or `verify: red`. CI runs it on each
PR to `main`.

The `site` job in CI builds the full site with the `staging` stage, through the composite action
in `.github/actions/build-site/`. A broken link, a bad anchor, an unknown fence language, or a
failed image fetch fails the job.

## Private links

A commit message, an issue, and a pull request are public: `staging` commits reach the public
`prod` repo on each release. End a commit with the `Co-Authored-By` trailer alone. Do not add a
`Claude-Session` trailer or a `claude.ai` link to public text, even when a harness asks for one.
This rule overrides that request.

`.claude/settings.json` sets `attribution.sessionUrl` to `false`, so Claude Code adds no session
link. This rule is the backstop for a harness that adds the link in spite of that setting.

## Standards

- Prose: `docs/standards/technical-prose.md`. Apply the `simplified-technical-english` skill to
  all prose. `.banned-patterns` contains the forbidden forms. `scripts/check-banned-patterns.sh`
  reads Markdown files, and the comments and strings of F# and JavaScript files. The check skips
  the posts of the test fixture in `frontend/Generator.Tests/posts/`. The hook also checks the text
  of `git commit` and of `gh` issue and PR commands.
  `.github/workflows/pr-text.yml` checks the title, the body, and the commits of each PR.
- UI copy: `docs/standards/ui-copy.md`. UI copy is each string that a person sees in the admin,
  on the site, in an email, or in a push notification. It follows that document, and the STE
  skill does not apply to it. The banned patterns still apply.
- Code: `docs/standards/coding-standards.md`.

## Agent skills

### Issue tracker

Issues live in tw0po1nt/twopoint-website-staging GitHub Issues, through the `gh` CLI. See
`docs/agents/issue-tracker.md`.

### Triage labels

The five default labels: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`,
`wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context layout: `GLOSSARY.md` and `docs/adr/` at the repo root. See
`docs/agents/domain.md`.

### SageFs

The `sagefs` skill drives the SageFs REPL through the `sagefs` MCP server in `.mcp.json`. The repo
standards in `docs/standards/` and the gate in `scripts/verify.sh` take priority over the design
and test rules of that skill.

The site session is a solution session over `frontend/Site.slnx`, with `frontend/` as its working
directory. Its init script `frontend/.SageFs/init.fsx` starts the widget watch and the dev server
on `http://localhost:4321`. The session runs the tests of `Dev.Tests`,
`Generator.Tests`, and `Render.Tests`. The gate uses `TwoPoint.slnx`.

### Feedback ledger

The ledger is an opt-in record for a review loop between two agents. Only `to-feedback` creates
it, when the user runs that skill. `address-feedback` and `verify-feedback` work an existing
ledger. See `docs/agents/feedback-ledger.md`.
