# Host the admin on Azure Static Web Apps at its own origin

The site is on GitHub Pages. The admin is one app, and the `staging` repo deploys it together with
the backend ([Share one post store and one admin across the stages](0002-share-one-post-store-and-one-admin-across-stages.md)).

We host the admin on the Free plan of Azure Static Web Apps, at `admin.twopoint.dev`. The admin
gets its own origin, apart from the blog pages. The host sends `X-Robots-Tag` and
`Cache-Control: no-store` for the MSAL bridge page. Its fallback route lets the admin use path
routes.

## Considered options

- **A path on the staging Pages site** (`staging.twopoint.dev/admin/`). It needs no new resource,
  domain, or CORS origin. The staging site build deploys it, so a failed staging build also stops
  an admin deploy. The tokens and the push permission then share the origin of the blog. GitHub
  Pages sends a fixed set of headers, so the bridge page has a cache of 10 minutes.
- **A separate Pages site.** A repo has one Pages site, so the admin needs a second repo and a
  token to push each build there. The header limits are the same as for the first option.

## Consequences

- The admin bundle is public. The token checks in `backend/Http/Endpoints/Auth.fs` are the only
  access control.
- The Function App allows the origin `https://admin.twopoint.dev`.
- `backend-staging.yml` deploys the admin after the backend, with a deploy token. The job runs only
  in the `staging` repo.

Detail: [Choose the admin host and sign-in](https://github.com/tw0po1nt/twopoint-website-staging/issues/195).
