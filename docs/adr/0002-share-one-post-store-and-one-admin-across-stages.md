# Share one post store and one admin across the stages

The site has three stages: local, staging, and production. Code moves from `staging` to `prod`
through a release PR. The backend is one Azure Functions app for staging and production, and
`backend-staging.yml` deploys it from `staging` at each merge. Posts are in a private Blob store
([Keep posts in a private Blob store](0001-keep-posts-in-a-private-blob-store.md)), so a release
PR has no posts.

We keep one post store for staging and production. Each publish starts `site.yml` in the two site
repos, and each build reads the same `published` container. The production run is the deploy of
record. A failed staging run is a warning. It shows that unreleased code breaks a live post. The
admin is one app. The `staging` repo deploys it together with the backend, and the `prod` repo
builds no admin.

## Considered options

- **One store for each stage.** Staging gets its own posts for tests. The backend has one post
  table for the two stages, so the table must get a stage key. Staging posts then become different
  from production posts, and a staging build no longer tests new code against the live posts.
- **Drafts on staging.** The staging build also reads `working`, so the author sees a draft in
  the full site. staging.twopoint.dev is public, so each draft becomes public. The admin preview
  uses the backend render, which gives the same output as the build.
- **An admin from `prod`.** The admin goes through a release, as the site does. The backend
  deploys from `staging` before each release, so each endpoint change must stay compatible with
  the previous admin until the release.

## Consequences

- A post goes live in a stage with no release PR. Only code changes go through a release.
- The CI `site` job signs in to Azure on each PR and builds the PR against the live posts.
- `dotnet test` reads a fixed copy of the 10 posts from before the admin. It does not read the
  store.
- The local stage reads posts that `pull` copies from the store with the sign-in of the developer.
  The local backend writes posts to Azurite and does not start `site.yml`.

Detail: [Choose how the stages separate posts and the admin](https://github.com/tw0po1nt/twopoint-website-staging/issues/194).
