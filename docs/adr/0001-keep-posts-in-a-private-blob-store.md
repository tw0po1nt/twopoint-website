# Keep posts in a private Blob store

The author writes and publishes each post in the web admin, with no commit and no local build. A
draft, a pending edit, and a revision must stay private. The `prod` repo is public, and each
release merges `staging` into `prod`. Thus a post in Git is public, and a commit to one repo
reaches the other repo only through a release.

We keep each post in a private Azure Blob Storage account. A post is a Markdown file with YAML
front matter, as in `frontend/content/posts/` before the admin. The backend writes the store and
starts `site.yml` with `workflow_dispatch`. The build downloads the published posts into a folder,
and then builds the site from that folder.

## Considered options

- **A commit to Git through the GitHub API.** The build and the tests stay the same. A draft in
  the public `prod` repo is public. A commit to `staging` reaches twopoint.dev only at the next
  release, and a commit to `prod` goes around the pull request flow.
- **Drafts in Azure, and a commit to Git at each publish.** Drafts stay private. The commit has the
  same two-repo problem as the first option.
- **A private content repo.** Posts stay in Git with their history. The approach adds a third repo,
  a read token in each site repo, and another place for drafts and pending edits.
- **Azure Table Storage.** The backend already uses tables. The largest post is too large for one
  Table string property.

## Consequences

- The site repos contain no posts. The local stage and `dotnet test` need a separate source of
  posts.
- Each `site.yml` run signs in to Azure before the build.
- The backend writes its post table at each publish. It does not read `rss.xml` to find new posts.

Detail: [Choose the post store and the publish pipeline](https://github.com/tw0po1nt/twopoint-website-staging/issues/193).
