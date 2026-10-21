# Keep each series as its own record

Before the admin, each post of a series has a copy of the series in its front matter: the slug, the
title, the excerpt, and the image. No check makes the copies agree. In the admin, the author must
make a series, then put posts in it, and must change the title or the excerpt in one place.

We keep each series as one record in the post store. The front matter of a post has only
`series: <slug>`. A series has no draft, no pending edit, and no revision. A save of a series writes
its record and starts a rebuild. The site shows a series only when it has a published post. A term
(a category or a tag) has no record. It stays as a title in the front matter of each post.

## Considered options

- **A copy in each post, as before.** The admin copies the fields of a series into the post, and a
  copy that does not agree is an error. A change to a series title then needs a pending edit and a
  publish on each post of the series.
- **A copy in each post, and the first post owns the fields.** The admin ignores the copies in the
  later posts. The front matter then contains data that the site does not use.
- **A record for each term too.** A term has only a title, and its slug comes from the title. A
  record adds a page and a store for no new data. Two titles for one slug give a warning, and the
  site shows the title of the newest published post.

## Consequences

- The post format changes. The build pulls the series records together with the published posts.
- The admin has a Series page, and the Details sheet of the editor makes a series in the series
  picker.
- A post in a series has the category `Series`. The editor sets it and locks the category picker.

Detail: [Define the rules for a series and a term in the editor](https://github.com/tw0po1nt/twopoint-website-staging/issues/216).
