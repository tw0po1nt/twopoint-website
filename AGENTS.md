# AGENTS.md

Instructions for agents in this repo.

This repo contains the code for twopoint.dev, a technical blog. `frontend/` is an F# static site
generator with F# widgets that Fable compiles to JavaScript
(`docs/spec/124-build-twopoint-dev-with-fsharp.md`). `backend/` is an F# Azure Functions app for
newsletter subscriptions, notifications, and posts.

Each post is a Markdown file in `frontend/content/posts/`.

To build the site, first run `npm ci` and `npm run build` in `frontend/Widgets/`. That build
compiles the widgets with Fable and writes `frontend/Widgets/dist/`. `npm test` in
`frontend/Widgets/` then runs each widget entry of `dist/` in happy-dom. Then run
`dotnet run --project frontend/Generator -- build --stage <local|staging|production>`. The command
writes the site to `frontend/dist/website/`, the newsletters to `frontend/dist/newsletter/`, and
the stage marker to `frontend/dist/stage`. The stage marker contains the stage name of the build.
After a production build, `dotnet run --project frontend/Generator -- upload --container <uri>`
sends each newsletter to Azure Blob. The command stops if the stage marker is not `production`.

`.github/workflows/site.yml` builds the site and deploys `frontend/dist/website/` to GitHub Pages
on each push to `main`. The repo name sets the stage: the `prod` repo is `production`, and the
`staging` repo is `staging`. On `production`, the workflow uploads the newsletters before the
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
  reads Markdown files, and the comments and strings of F# files. The check skips the blog posts
  in `frontend/content/posts/`. The hook also
  checks the text of `git commit` and of `gh` issue and PR commands.
  `.github/workflows/pr-text.yml` checks the title, the body, and the commits of each PR.
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

### Feedback ledger

The ledger is an opt-in record for a review loop between two agents. Only `to-feedback` creates
it, when the user runs that skill. `address-feedback` and `verify-feedback` work an existing
ledger. See `docs/agents/feedback-ledger.md`.
