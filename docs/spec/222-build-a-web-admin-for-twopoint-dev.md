Source: https://github.com/tw0po1nt/twopoint-website-staging/issues/222

## Problem Statement

The author writes each post as a Markdown file in Git. To publish a post, the author makes a commit, builds the site, opens a PR to `staging`, and then opens a release PR to `prod`. A typo fix takes the same steps. A draft in Git reaches the public `prod` repo at the next release.

The author moderates comments in an Android app. The app is already retired, so the author has no working tool to approve a comment, to ban a commenter, or to start a new-post delivery. A new post also needs a manual row in the post table, or the backend rejects each comment on it. The author cannot see the subscriptions, and gets no notification of a new comment.

## Solution

A web admin at `admin.twopoint.dev` replaces the Android app. The author signs in with Entra ID and then:

- Writes a post in a calm Markdown editor, with a preview that matches the site, and publishes it. The publish starts a rebuild of the static site, with no commit and no local build.
- Moderates comments and commenters from one moderation queue.
- Starts a new-post delivery after the post is deployed.
- Sees and changes the subscriptions.
- Uploads images and PDFs into a post.
- Gets a notification on each registered device when a comment arrives.
- Sees a dashboard with the work that waits and the audience.

The posts move out of Git into a private Azure Blob post store. The site stays static on GitHub Pages, so SEO, the RSS feed, and the newsletters keep their current form.

The map [Wayfinder: a web admin for twopoint.dev with dynamic posts](https://github.com/tw0po1nt/twopoint-website-staging/issues/186) contains the decisions. Each ticket of the map contains the detail and the reasons for its decisions. When this spec and a ticket disagree, this spec is correct.

## User Stories

### Sign-in

1. As the author, I want to sign in to the admin with my Entra ID account, so that only I can change the blog.
2. As the author, I want the admin to send me to the Entra sign-in when I open it with no session, so that I need no separate sign-in page.
3. As the author, I want a "Sign in again" banner when my session expires, so that I keep the unsaved text in the editor.
4. As the author, I want my session to be shared across admin tabs, so that I sign in one time.
5. As the author, I want no consent prompt at sign-in, so that the sign-in is one step.
6. As the author, I want the admin to stay out of search engines, so that nobody finds it from a search.

### Posts: the post list

7. As the author, I want a list of all posts with the state of each one, so that I see drafts, published posts, and upcoming posts in one place.
8. As the author, I want each post in the list to show "Deploying", "Deployed", or "Deploy failed", so that I know when the site shows my change.
9. As the author, I want the post list to show the comment counts of each post, so that I see where readers talk.
10. As the author, I want the filters "Drafts", "Pending edits", and "Deliveries to start" on the post list, so that I find the work that waits.
11. As the author, I want to make a new draft from the post list, so that I can start a post at any time.

### Posts: the editor

12. As the author, I want a calm editor with no site theme and no admin navigation, so that I can concentrate on the text.
13. As the author, I want one column with the title, the excerpt, and the Markdown body, so that I write the post as readers read it.
14. As the author, I want the Markdown source with quiet styles for headings, emphasis, links, and code, so that I see the structure with no WYSIWYG changes to my Markdown.
15. As the author, I want the top bar to fade while I type, so that it does not distract me.
16. As the author, I want one line of status text, for example "Draft · saved" or "Published Feb 4, 2026 · changes not live · saving…", so that I always know the state of the post.
17. As the author, I want the editor to save about 2 seconds after I stop typing, and at once with ⌘S, so that I never lose text.
18. As the author, I want a save from an old browser tab to fail, so that it never replaces newer text.
19. As the author, I want one primary action, "Publish" for a draft and "Publish changes" for a pending edit, so that the next step is clear.
20. As the author, I want a "•••" menu with the other actions for the state, so that the top bar stays small.
21. As the author, I want each action that is not available to show the reason, so that I know what to do first.
22. As the author, I want a Details sheet with the slug, the category, the tags, the series, the card image, the author, and "Show on the home page", so that the metadata stays out of the writing column.
23. As the author, I want Preview to show the rendered article in the place of the text, and Esc to go back, so that I check the result quickly.
24. As the author, I want the preview to use the same render, highlight, and heading ids as the build, so that the preview is what readers see.
25. As the author, I want the preview to use the text in the editor, also before a save, so that I see my latest change.
26. As the author, I want to use the editor on a phone, so that I can fix a typo away from my desk.
27. As the author, I want "Add image" in the "•••" menu on a phone, so that I can add an image with no paste.

### Posts: problems

28. As the author, I want each save to return the problems of the post, so that I see them while I write.
29. As the author, I want a small dot on "Details" and on the primary action when problems exist, so that the writing column has no error text.
30. As the author, I want each problem to link to its field, or to move the cursor to its body line, so that I fix it quickly.
31. As the author, I want an error to block the publish and a warning to allow it, so that only a real defect stops me.
32. As the author, I want the same problems in the admin and in the build, so that a post that passes in the admin also builds.
33. As the author, I want a change that breaks a link in another post to be an error on my post, with each link to fix, so that no published post gets a broken link.
34. As the author, I want a warning for an image with an empty alt text, so that each image is accessible.
35. As the author, I want a warning for an image URL outside the asset container, so that I know which images I do not control.

### Posts: lifecycle

36. As the author, I want to publish a draft now or on a future date, so that I can publish before a talk.
37. As the author, I want an upcoming post to be on the site with its badge, so that readers can open it before the date.
38. As the author, I want an edit to a published post to be a pending edit until I select "Publish changes", so that readers never see half a change.
39. As the author, I want to see the changes from the published content before I publish them, so that I know what goes out.
40. As the author, I want to set the update date when I publish changes, so that readers know the post changed.
41. As the author, I want to discard a pending edit, so that I can abandon a change.
42. As the author, I want to unpublish an upcoming post, so that I can stop a post before its date.
43. As the author, I want to change the publish date of an upcoming post, so that I can move a post.
44. As the author, I want the slug of a draft to be editable and the slug of a published post to be fixed, so that no public URL breaks.
45. As the author, I want to delete a post after I type its slug, so that I cannot delete a published post by accident.
46. As the author, I want a revision at each publish, and a way to restore a revision as a pending edit, so that I can go back to an earlier text.

### Posts: series and terms

47. As the author, I want to make a series from the series picker in the Details sheet, so that I do not leave the editor.
48. As the author, I want a Series page that lists, makes, and edits each series, so that I manage the series in one place.
49. As the author, I want a series save to go to the site with a rebuild, so that a title fix needs no edit of each post.
50. As the author, I want the series slug to lock while a published post is in the series, so that `/series/<slug>/` does not break.
51. As the author, I want to delete only a series with no post, so that no post refers to a missing series.
52. As the author, I want the editor to set the category `Series` when I pick a series, so that the series rules stay correct.
53. As the author, I want the category and tag pickers to suggest the current terms with their number of posts, so that I use the same terms again.
54. As the author, I want the pickers to match a typed term by slug, so that `functional Programming` gives the existing `Functional programming`.
55. As the author, I want a warning when two posts give two titles for one term slug, so that the site shows one title.
56. As the author, I want a warning when a post has no tag for its publish year, so that the year tags stay complete.

### Posts: assets

57. As the author, I want to paste or drop an image into the editor and see it upload at once, so that I add an image in one step.
58. As the author, I want the cursor in the alt text after an upload, so that I write the alt text at once.
59. As the author, I want a dropped PDF to become a `::pdf` line, so that I add slides in one step.
60. As the author, I want a failed upload to show a retry, so that I do not lose the image.
61. As the author, I want the admin to remove the location and camera data from a photo, so that I do not publish private data.
62. As the author, I want a second upload of the same file to give the same URL, so that the store has no copies.

### Deploys

63. As the author, I want the editor and the post list to show the deploy state, and to update every 10 seconds during a deploy, so that I know when the site shows my change.
64. As the author, I want a "Rebuild site" action on the dashboard, so that I can retry a failed deploy.
65. As the author, I want a notification when a production deploy fails, so that I know at once.
66. As the author, I want a failed staging run to show as a warning on the dashboard, so that I see when unreleased code breaks a published post.

### New-post deliveries

67. As the author, I want to start a new-post delivery as a separate action, with an optional intro, so that I choose when subscriptions get the email.
68. As the author, I want the action to stay disabled until the publish date and the deploy of the latest change, so that the email always agrees with the page.

### Moderation

69. As the author, I want a moderation queue of the new comments on all posts, oldest first, so that I work through them in order.
70. As the author, I want each row to show the post title, the commenter name, the email address, the commenter status, and the text, so that I decide with no other page.
71. As the author, I want to approve, reject, or delete a comment from the row, so that one click moderates it.
72. As the author, I want "approve and trust" on a row, so that I trust a good commenter in the same step.
73. As the author, I want an approval to change only the comment, so that I decide trust separately.
74. As the author, I want a rejected comment to stay in the admin, so that I can approve it later.
75. As the author, I want delete to remove a comment permanently after a confirmation, so that I can remove spam or personal data.
76. As the author, I want the comments of one post with a filter by approval, so that I review a post.
77. As the author, I want a commenter page with the status, the first date, and each comment, so that I judge a commenter.
78. As the author, I want a commenter list with a filter by status, so that I find a banned commenter.
79. As the author, I want to trust, untrust, ban, unban, and delete a commenter, so that I control who comments.
80. As the author, I want a ban to reject the new comments of the commenter and keep the approved ones, so that I decide each old comment separately.

### Notifications

81. As the author, I want a notification on each registered device when a comment arrives, so that I moderate quickly.
82. As the author, I want a different title for a comment that waits and for a comment from a trusted commenter, so that I know whether to act.
83. As the author, I want a click on a notification to open the comment in the admin, so that I act in one step.
84. As the author, I want the app badge to show the number of new comments, so that I see the queue size from the Home Screen.
85. As the author, I want to turn on notifications from a Settings page and see each registered device, so that I control where notifications go.
86. As the author, I want to remove a registered device, so that an old browser gets no notifications.
87. As the author on an iPhone, I want the admin to tell me to add it to the Home Screen, so that I can turn on notifications.

### Subscriptions

88. As the author, I want a list of all subscriptions with a search and an Active or Inactive filter, so that I find a subscription.
89. As the author, I want the counts of active and inactive subscriptions on the Subscriptions page, so that I see the size of the audience.
90. As the author, I want a detail sheet with the canceled date and the deliveries of a subscription, so that I see its history.
91. As the author, I want to cancel or delete a subscription with no email to the reader, so that I can act on a request by email.

### Dashboard

92. As the author, I want a to-do row with the moderation queue, drafts, pending edits, and deliveries to start, so that I see the work that waits.
93. As the author, I want each count to link to its page, so that I go to the work in one click.
94. As the author, I want an audience tile with the active subscriptions and the new and canceled subscriptions in the last 30 days, so that I see the trend.
95. As the author, I want the 10 posts from before the admin never to count as a delivery to start, so that the count shows only real work.

### Readers and commenters

96. As a reader, I want each public URL, the RSS feed, and the sitemap to stay the same after the move to the post store, so that no link breaks.
97. As a reader, I want a draft never to show on staging.twopoint.dev or twopoint.dev, so that I read only published posts.
98. As a trusted commenter, I want my comment to show at once with "Your comment is posted.", so that I see it on the post.
99. As a new commenter, I want the widget to say that my comment shows after approval, as today, so that I know what happens.
100. As a commenter, I want no email from the blog about a moderation action, so that my inbox stays quiet.
101. As a reader with a subscription, I want each newsletter to agree with the page, so that the email has the latest text.
102. As a reader with a subscription, I want a newsletter of a deleted post to go away, so that the email links do not show a removed post.
103. As a new reader with a subscription, I want the welcome to contain the latest deployed post, so that its link works.

### The developer

104. As the developer, I want `scripts/verify.sh` to test the admin, the render, and the backend actions with .NET only and no network, so that the gate stays fast.
105. As the developer, I want `dotnet test` to read a fixed copy of the 10 posts, so that the tests never read the store.
106. As the developer, I want CI to build each PR against the posts in the store, so that a PR that breaks a published post fails.
107. As the developer, I want a `pull` command for the local stage, so that I build the site with the real posts.
108. As the developer, I want the local backend to use Azurite and to log each dispatch, so that a local publish never changes the real store.
109. As the developer, I want the local admin on `http://localhost:4322`, so that I test it against the local backend.
110. As the developer, I want a `diff -r` of the build output in each migration PR, so that I know that the site stays the same.
111. As the developer, I want a checklist and a wizard for each manual Azure, Entra, and GitHub step, so that I can repeat the setup.

## Implementation Decisions

### Order of the work

The work has five parts. The parts must keep this order:

1. **Migration step 1: the assets.** A one-time script copies each old asset into `assets` and changes the URLs in the 10 posts in Git. Then a release to `prod`.
2. **Migration step 2: the posts.** A wizard sets up the post store. A one-time script uploads the posts and the series records. The build pulls from the store. Then a release to `prod`.
3. **Clean-up.** A manual step deletes the old containers. A PR deletes the two scripts and the wizard.
4. **The shared library.** The render and the checks move out of `Generator` into a new library. This part can run in parallel with parts 1 to 3. It must come before each admin endpoint that renders or checks a post.
5. **The admin.** The admin endpoints, the admin screens, the Web Push work, and the Android removal.

The admin endpoints for posts come after step 2 is live in production. The backend and the admin deploy from the `staging` repo at each merge to `main`. Thus each slice of part 5 must keep the blog, the comments, and the subscriptions working. A site change in part 5 reaches production at the next release to `prod`.

### Migration

**Step 1: the assets.**

- A PR to `staging` adds a one-time F# script. The developer runs it one time with `az login`.
- The script downloads each old asset from `posts`, `series`, and `thumbnails`, and the two external `og:image` sources (the Unsplash photo and the YouTube thumbnail). It uses the same request as the build image fetch, because the Unsplash bytes change with the `Accept` header.
- It names each file from its SHA-256 and writes it to `assets` with the correct `Content-Type` and `Cache-Control`. It keeps the bytes as they are, with the metadata.
- It changes the URLs in the 10 posts. The PR has the script, the 10 changed posts, the URL map, and the tests that name an old URL.
- A release to `prod` uploads each newsletter again with the new URLs.

**Step 2: the posts.**

1. A `wizard` script walks the author through the Azure set-up: the new private storage account, its containers, the roles, and the federated credentials for `pull_request` and `workflow_dispatch` runs in the two repos.
2. A one-time F# script uploads the 10 posts to `published`, one revision for each post to `revisions`, and the 2 series records to `series`. Each write sends `If-None-Match: *`. The posts get the store format: `series: <slug>` and no `draft` field. Each `published` blob gets the migrated mark.
3. A PR to `staging` adds the `pull` command, adds the Azure sign-in and `pull` to the build action, `site.yml`, and the CI `site` job, and adds `workflow_dispatch` to `site.yml`. It moves the 10 posts with `git mv` into a test fixture in `Generator.Tests`, in the store format. Git then ignores `frontend/content/posts/`.
4. The merge. The staging site builds from the store.
5. A release to `prod`. The production site builds from the store.

Rules:

- No change to a post from step 2.2 to step 2.5.
- The revision of a migrated post is `<slug>/<publishDate>.md`. Its content is the same as the `published` blob. The old Git versions do not become revisions.
- The test fixture contains the posts after step 1, in the store format.
- The old containers stay until step 2 is live in production.

**Between step 2 and the admin,** a new post or a fix needs three manual steps:

1. `az storage blob upload` to `published`, with the migrated mark.
2. `az storage entity insert` of its post-table row (slug, title, created date). Without the row, the backend rejects each comment on the post.
3. A manual run of `site.yml` in the two repos.

**The check that the site stays the same.** Before each step, build the website and the newsletters from `main`. After the change, build them from the branch. Compare the two folders with `diff -r`, and put the result in the PR body. For step 1, first apply the URL map to the folder from `main`. For step 2, the diff must be empty with no map. Run the two builds one after the other, when no post is upcoming. The repo gets no snapshot test.

### Post store

- A new storage account, separate from `twopointwebsite`. Blob public access is off for the whole account.
- Each version of a post is one `.md` blob, with the YAML front matter and the body. The blob name gives the slug.

| Container | Blob | Contents |
| --- | --- | --- |
| `published` | `<slug>.md` | The content of a published post |
| `working` | `<slug>.md` | A draft, or the pending edit of a published post |
| `revisions` | `<slug>/<publish time>.md` | A copy of the content at each publish |
| `series` | `<slug>.yml` | One series record: the title, the excerpt, and an optional image |

- The location of the blobs gives the state of a post. A post with no `published` blob is a draft. A published post with a `working` blob has a pending edit. The `draft` front matter field goes away.
- Each write sends the ETag in `If-Match`. A write from an old browser tab fails, and the newer content stays.
- `working` is a store name only. The spec and the admin text use **Draft** and **Pending edit**.
- One store for all stages, with no stage key. The staging build and the production build read the same `published` and `series` containers.
- The build identity has Storage Blob Data Reader on `published` and `series` only. It cannot read a draft, a pending edit, or a revision. Thus staging never shows a draft.
- The backend identity can read and write the four post store containers.

### Post lifecycle

A post has two states: **Draft** and **Published**. An upcoming post is a published post with a publish date in the future. No hidden scheduled state exists.

| Action | Rule |
| --- | --- |
| Create, save | A draft has no publish date. A save writes the `working` blob only. On a published post, a save changes the pending edit. |
| Preview | In the admin only, with no public URL. |
| Publish | The author selects "now" (the publish date is the publish time) or a future date. |
| Publish changes | The pending edit becomes the published content. The author can set the update date. |
| Discard changes | Deletes the pending edit. |
| Unpublish | Only for an upcoming post. The post becomes a draft. The draft text is the pending edit, if one exists, or else the published content. The publish date is removed. |
| Change the date | Only for an upcoming post. The new date can be in the past, and then the post is not upcoming. |
| Change the slug | Only for a draft. |
| Delete | Possible for each post. A published post needs a typed confirmation of its slug. Its comments are deleted. Its delivery records stay as history. |
| Revisions | One revision at each publish and each "publish changes". The author can restore a revision as a pending edit. |

A publish does these steps in this order:

1. Write the `revisions` blob.
2. Write the `published` blob.
3. Write the row in the post table.
4. Delete the `working` blob.
5. Send `workflow_dispatch` to `site.yml` in the two site repos.

"Publish changes", unpublish, a date change, and the delete of a published post follow the same order, with the blob changes of each action. The post table gets the row at a publish, a new title at "publish changes", a new created date at a date change, and no row after an unpublish or a delete.

If the dispatch fails, the post stays published, and the admin shows "Deploy failed".

Publish, publish changes, unpublish, and delete run the check again on the server, and refuse on an error.

### Publish pipeline and deploy state

- A private GitHub App with only Actions: write, installed on the two site repos. Its private key stays in Key Vault, and Key Vault signs the JWT. The backend gets an installation token of 1 hour for each dispatch.
- The production run is the deploy of record. A failed staging run is a warning only.
- The app subscribes to `workflow_run`. A new public backend endpoint receives the webhook. It checks `X-Hub-Signature-256` with a webhook secret in Key Vault. It writes the repo, the run number, and the state of each run to a table.
- Each post change stores the production run number from the `workflow_dispatch` reply.
- A change is **deployed** when a successful production run of `site.yml` has that run number or a higher one. Each run pulls the newest blobs, so a later run includes each earlier change. A push to `main` also counts.
- A pending run that a later run cancels in the `pages` group shows as "replaced". The later run deploys the change.
- If the dispatch fails, the change has no run number. It is deployed after a successful run that the webhook reports as queued after the change.
- Each post shows "Deploying", "Deployed", or "Deploy failed". A status endpoint gives the state. The admin reads it every 10 seconds while a change is not deployed and its run is not done, and at each page load.
- The dashboard has one "Rebuild site" action, which dispatches `site.yml` in the two repos. No post has its own rebuild action.
- A failed production run shows on the dashboard with a link to the run in GitHub, and sends a Notification with a link to the dashboard. A successful run sends no notification. A failed staging run shows only on the dashboard.
- A series save dispatches a rebuild in the same way, and the Series page shows the deploy state.

### Build and generator

- A new generator command, `pull`, downloads each blob in `published` and in `series` into a folder that Git ignores. `build` reads that folder. If the folder is empty, the build fails and tells the developer to run `pull`.
- `site.yml` and the CI `site` job sign in to Azure through OIDC and run `pull` before `build`. The build identity trusts `pull_request` and `workflow_dispatch` runs in the two repos.
- `site.yml` accepts `workflow_dispatch`.
- `upload` also deletes each `posts/<slug>/index` blob that has no post in the build. A delete or an unpublish then removes the newsletter in the same run that removes the page.
- A new generator command, `preview-styles --out <folder>`, writes the site CSS and its fonts into the admin build output.
- The post format: `series: <slug>` refers to a series record. The build fails on a slug with no record.
- The site shows a series only when it has a published post.
- A term title: the site shows the title of the newest published post with that slug, on each page. This change changes the output for 2 tag slugs, so it goes in part 5, after the migration.

### The shared library

- A new library project contains these modules: Slug, Model, FrontMatter, Highlight with its grammar files, Directive, Images, Body, Post, SitePath, Links, and the article part of PostPage.
- `Generator` and the backend reference the library. PreMailer, Azure Blob, Assets, OgImages, the pages, Newsletter, Upload, and the CLI stay in `Generator`.
- SkiaSharp and its Linux native package go into the library, because the image size code uses them.
- `Post.parse` takes the slug from the blob name. It has no `Draft` case, and it renders each post.
- `privacy.md` becomes an embedded resource of the library, so the backend has the heading ids of the privacy page.
- One library function gives each site path from the posts and the series: the fixed pages, the post pages, and the category, tag, and series list pages. The render map uses the same function.
- The library takes the stage as an input. An Azurite URL is an error on each stage except local.
- The tests of the moved modules move with them.

**The check.** One library function takes the stage, an image size port, the series records, the published posts, and the slug and text of one post. It returns the article HTML and the problems.

- The current text of the post replaces its published version in the set. The check then runs the link check on all posts, as the next build does.
- A change to post A that breaks a link in post B is an error on post A, with each link to fix. A problem in post B that post A does not cause does not show on post A.
- The link check parses the other posts with an image stub, so it downloads nothing. The backend caches each parse by the ETag of its blob.
- The backend gets each image over HTTP for its size, with one memory cache for each Function App instance. Each image request gets 10 seconds and no retry. The build keeps 30 seconds and 3 attempts. A failure gives the same error as the build.
- The backend does not make the `og:image` crop.
- The backend deploys from the `staging` repo, so the preview and the checks use the staging code, and the production build uses the code of the last release. The two can differ until a release. The failed staging run warning shows this case.

**The preview** shows the article only: the title, the meta block, the outline, and the body. It has no site header, footer, widgets, comments panel, or older and newer cards. The preview frame links the site CSS from the admin build output.

### Problems

- Each problem has a severity: Error or Warning. An error makes the build fail and blocks a publish. A warning does neither. The build prints each warning.
- Each problem has a place: a front matter field, a body line counted from the first line of the body, or the whole post. The build changes a field or a body line back to `file:line`, so its error output does not change.

| Field | Problem | Severity |
| --- | --- | --- |
| series | No series has this slug. | Error |
| category | The post has no category. | Error |
| category | The post has a series, and the category is not `Series`. | Error |
| category | The category is `Series`, and the post has no series. | Error |
| tags | The post has one slug two times. | Error |
| category or tags | Another published post has the same slug with a different title. The problem names the other posts. | Warning |
| tags | No tag for the publish year, or a year tag for another year. Only for a post with a publish date. | Warning |
| body | An image with an empty alt text. | Warning |
| body | An image URL outside `assets`. | Warning |
| body | An Azurite URL on a stage other than local. | Error |
| body | A link that the change breaks in another post. | Error |

The current build errors stay errors. A post with no tags has no problem. A draft can save with no category.

### Series and terms

- A series is one record in the post store. A series has no draft, no pending edit, and no revision. A save writes the record and dispatches a rebuild.
- The series picker in the Details sheet ends with "New series…". It opens a short form (title, slug, excerpt, image), saves the record, and selects the series.
- A Series page in the admin navigation lists each series, makes one, and edits one. It lists the posts of each series in publish date order, and the drafts after them. The image uses the upload endpoint.
- The form gives the slug from the title with the term slug rule, and the author can change it. The slug is locked while a published post is in the series. The form does not save a slug that another series has. The form shows its own errors: an empty title, an empty excerpt, a bad slug, or a slug that another series has.
- Order: publish date. An upcoming post counts. A draft has no position.
- A change of the series of a published post is a pending edit. If the last published post leaves a series, `/series/<slug>/` goes away, and the link check gives an error for each link to it.
- Delete only a series that no published post, draft, or pending edit refers to. Otherwise the Series page shows the reason and the posts.
- When the author picks a series, the editor sets the category to `Series` and locks the category picker. When the author removes the series, the post has no category. `Series` is not in the category picker list.
- A term keeps its title in the front matter. It has no record and no admin page. The picker suggests each term on the published posts, with its number of posts. The author can type a new term. The picker matches by slug.
- A category is required. A new draft has no category.

### Assets

- A public container `assets` on `twopointwebsite`. The container cannot be listed.
- The blob name is `<first 16 hex characters of the SHA-256>.<ext>`. The same file gives the same name. A blob never changes.
- The Markdown contains `https://twopointwebsite.blob.core.windows.net/assets/<hash>.<ext>`. No custom domain.
- An asset is public at its upload, before the publish of its post.
- The admin POSTs the file to an admin endpoint. The backend does these steps in this order:
  1. Check the type from the first bytes: JPEG, PNG, WebP, GIF, or PDF. No SVG.
  2. Check the size: 25 MB at most.
  3. Remove the EXIF, XMP, and IPTC data from a JPEG, PNG, or WebP, and keep the orientation value. No re-encode and no resize.
  4. Calculate the hash of the result.
  5. Write the blob with the correct `Content-Type` and `Cache-Control: public, max-age=31536000, immutable`.
  6. Return the URL.
- The backend identity can write to `assets` only, on `twopointwebsite`.
- The backend never deletes an asset. The admin has no delete action and no Assets page.
- The build does not change. It gets each image over HTTP.

### Moderation

| Action | Rule |
| --- | --- |
| Post (new commenter) | The comment is new and goes into the moderation queue. |
| Post (trusted commenter) | The comment is approved at the time of the post. The response tells the widget, and the widget shows "Your comment is posted." and the comment. |
| Post (banned commenter) | A silent ban: normal messages, no verification email, no stored comment, and no notification. |
| Approve | Only the comment changes. The backend no longer makes the commenter trusted at an approval. |
| Reject | Readers do not see the comment. The blog keeps it, and the author can approve it later. |
| Delete comment | Removes the comment and its history permanently, after a confirmation. |
| Trust | Only the author makes a commenter trusted: a commenter action, or "approve and trust" on a comment row. |
| Untrust | The commenter becomes new. No comment changes. |
| Ban | Each new comment of the commenter becomes rejected. The approved comments stay. |
| Unban | The commenter becomes new. The comments that the ban rejected stay rejected. |
| Delete commenter | Removes the commenter and each comment of the commenter, after a confirmation. The address can verify again later. |

- The approval changes in these ways: new to approved, new to rejected, approved to rejected, and rejected to approved.
- The author cannot edit a comment.
- The blog sends no email to a commenter about a moderation action.
- Admin views: the moderation queue (each new comment on all posts, oldest first), the comments of one post with a filter by approval, a commenter page (the name, the email address, the status, the first date, and each comment), and a commenter list with a filter by status.
- The response of `Blog-Posts-PostComment` gets the approval of the comment. The Comments widget reads it.

### Notifications

- Standard Web Push (RFC 8030) with VAPID (RFC 8292). No FCM and no email.
- A sender in the backend, written with `System.Security.Cryptography`, with no Web Push library. It encrypts the payload with `aes128gcm` (RFC 8291 and RFC 8188), signs a VAPID JWT with ES256 with an expiry of less than one day, and sends a `TTL` header.
- One VAPID key pair. The private key is a secret in Key Vault. The public key is in the backend config and in the admin config. The JWT subject is a `mailto:` address. The local backend has its own key pair.
- A new `registeredDevices` table. The partition key is the user ID of the author. The row key is the SHA-256 of the endpoint. The columns are the endpoint, `p256dh`, `auth`, a label from the user agent (for example "Safari on macOS"), `CreatedDate`, and `LastSeenDate`.
- The backend sends to each registered device, and deletes the row on `404` or `410`.
- `PostComment` stores the comment and puts a message on a new `new_comment` queue. A queue handler sends to each registered device and logs each error other than `404` and `410`. A push failure never gives the commenter a `500`. Each send gets a row in the `pushes` table, as today.

| Case | Title | Body | Click opens |
| --- | --- | --- | --- |
| The comment waits for moderation | New comment on ‹post title› | ‹commenter name›: ‹first 120 characters› | `/comments#‹comment id›` |
| A trusted commenter, the comment is approved | Comment posted on ‹post title› | ‹commenter name›: ‹first 120 characters› | `/posts/‹slug›/comments#‹comment id›` |

- The comment payload contains the case, the comment ID, the post slug, the post title, the commenter name, the text, and the count of new comments. It stays below 4 KB. The worker builds the notification. The tag is the comment ID, so a queue retry replaces the notification.
- If an admin tab is open, a click brings it to the front and opens the target.
- The worker calls `navigator.setAppBadge` with the count of new comments, and sends a message to each open admin tab. The tab then refreshes the count. When the moderation queue is empty, the admin clears the badge.
- A Settings page has a Notifications section. "Turn on notifications on this device" calls `Notification.requestPermission` and `pushManager.subscribe` from the click, then sends the subscription to the backend. The section lists each registered device with its label, the date added, and the last use, and a Remove button.
- At each load with the permission `granted`, the admin sends its subscription again with no prompt. The worker sends a new subscription after a `pushsubscriptionchange` event. With the permission `denied`, the section tells the author to change the browser setting.
- The admin has a `manifest.webmanifest` with `display` `standalone`, `start_url` `/`, `scope` `/`, an `id`, and icons. If `PushManager` is absent on iOS, the section says: "Add the admin to the Home Screen to turn on notifications."

### Subscriptions

- The author can cancel or delete a subscription. The author cannot add a subscription, reactivate one, or edit a name or an email address. A cancellation or a delete from the admin sends no email.
- A delete removes the subscription and all its deliveries, after a confirm dialog.
- One list of all subscriptions: the email address, the name, the created date, and active or inactive, newest first. A search of the email address and the name, and a filter All, Active, or Inactive, in the browser. The counts of active and inactive subscriptions. No paging and no export.
- A detail sheet: the fields, the canceled date, "Cancel subscription", "Delete", and the deliveries newest first (the type, the post title with a link, and the date). No send status.
- Each cancellation sets a new `CanceledDate`. A reactivation clears it. Each current inactive row gets the date from its `Timestamp`.

### New-post delivery and welcome

- The backend refuses the start of a new-post delivery with `409` until the publish date is in the past and the latest change to the post is deployed. The admin disables the action and shows the reason.
- The welcome uses the latest post whose publish is deployed. Between a publish and its deploy, a new subscription gets the previous post.
- The delivery records keep the slug only, so the delete of a newsletter breaks no history.

### Dashboard

| Row | Count | Data | Link |
| --- | --- | --- | --- |
| To-do | Moderation queue | The new comments on all posts | The moderation queue |
| To-do | Drafts | The posts with a `working` blob and no `published` blob | The post list, filter "Drafts" |
| To-do | Pending edits | The published posts with a `working` blob | The post list, filter "Pending edits" |
| To-do | Deliveries to start | The published posts with a publish date in the past, deployed, with no new-post delivery, and with no migrated mark | The post list, filter "Deliveries to start" |
| Audience | Active subscriptions, "+N new, −M canceled in the last 30 days" | `CreatedDate` and `CanceledDate` in the last 30 days | The Subscriptions page, filter "Active" |

- The migrated mark is blob metadata on the `published` blob. The migration script sets it on the 10 posts. The author does not set or clear it. Each later write of the `published` blob keeps it.
- The dashboard also shows the deploy state, the "Rebuild site" action, and the staging warning.
- The dashboard shows no comment activity, no delivery counts, and no commenter counts. The 30-day window is fixed.
- `GET internal/dashboard` returns the six numbers. The backend calculates them at each request, with no stored counters. The admin reads them once at each load of the dashboard.

### Backend endpoints

Each admin endpoint stays behind the Entra check. Android keeps no compatibility, so an endpoint that stays can change its contract.

| Endpoint | Result |
| --- | --- |
| `Admin-Posts-GetAll` (`GET internal/posts`) | Changes. Returns the admin post list: the slug, the title, the state, the pending edit flag, the publish date, the deploy state, the comment counts, and the fields for the three filters. |
| `Admin-Posts-Sync` (`POST internal/posts`) | Goes. The route is then free for a new draft. |
| `Admin-Posts-GetComments` | Stays. |
| `Admin-Posts-Comments-UpdateApproval` | Stays. The approval no longer makes the commenter trusted. |
| `Admin-Posts-Comments-Delete` | Stays. |
| `Admin-Notifications-Register` | Goes. |
| `Admin-Posts-Deliveries-Start` | Changes. It waits for the deployed latest change. |
| `Blog-Posts-PostComment` | Changes. Trusted and banned rules, the `new_comment` queue, and the approval in the response. |
| `Queues-ProcessNewPostMessage` and the `new_post` queue | Go. |

New endpoints:

- Posts: create a draft, get a post (the text, the ETag, the state, and the published text for "changes from live"), save, preview, publish, publish changes, discard changes, unpublish, change the date, change the slug of a draft, delete, list the revisions, and restore a revision.
- Comments: the moderation queue on all posts.
- Commenters: list, get one with each comment, change the status (trust, untrust, ban, unban), and delete.
- Series: list, save, and delete.
- Assets: upload.
- Registered devices: register or refresh, list, and remove.
- Deploys: the status, and "Rebuild site".
- The `workflow_run` webhook. It is public, with the signature check.
- Subscriptions: `GET internal/subscriptions`, `GET internal/subscriptions/{id}/deliveries`, `POST internal/subscriptions/{id}/cancellations`, and `DELETE internal/subscriptions/{id}`.
- `GET internal/dashboard`.

### Admin host and sign-in

- Azure Static Web Apps, Free plan, one app at `admin.twopoint.dev` with a `CNAME` record.
- The bundle is public. The token checks in the backend are the only access control. No SWA route role and no SWA sign-in.
- `staticwebapp.config.json` sends `X-Robots-Tag: noindex, nofollow` on each response. `index.html` also has a `noindex,nofollow` meta tag. No sitemap.
- Path routes, for example `/posts/<slug>` and `/comments`. `navigationFallback` serves `index.html` for each path with no file, except `/redirect/` and the bundle files.
- `/redirect/` is the MSAL bridge page. It sends `Cache-Control: no-store` and no COOP header.
- A new single-tenant registration, "TwoPoint Admin Web", with only `spa` redirect URIs `https://admin.twopoint.dev/redirect/` and `http://localhost:4322/redirect/`, and the delegated permission `access_as_user`. The API pre-authorizes it. The authority is the tenant of the author. The scope is `api://<API client ID>/access_as_user`.
- The API moves to v2.0 tokens. The backend check reads the client ID from `azp` only, with a map that has only the `web` entry. The check of the one user `oid` and of `scp` equal to `access_as_user` stays. An unknown user or client gets `403`.
- MSAL.js 5 through a hand-written binding, in the same pattern as the PDF.js binding. At page load with no account, the admin calls `loginRedirect`. In a session, `acquireTokenSilent` gets each token. On `InteractionRequiredAuthError`, the admin shows a "Sign in again" banner whose button calls `acquireTokenPopup`, and then runs the failed call again. The token cache is `localStorage`.
- CORS: add `https://admin.twopoint.dev` to the Function App, and `http://localhost:4322` to the local settings template. `supportCredentials` stays `false`.

### Admin app

- A new admin project in `frontend/`, with its own `package.json` and esbuild script, `index.html`, the bridge page, the push worker, the manifest, and `staticwebapp.config.json`. It references the Widgets project for shared code, such as the MVU helper and the API client. MSAL and the editor stay out of the site bundle.
- The admin project and an admin test project join `TwoPoint.slnx`.
- An admin config file has the public settings for two build modes. The dev mode uses `http://localhost:7071` and `:4322`. The release mode uses the Function App URL and `admin.twopoint.dev`. The esbuild script selects the mode.
- Screens: the dashboard, the post list, the editor, the moderation queue, the comments of a post, the commenter list, the commenter page, the Series page, the Subscriptions page, and Settings. Each screen except the editor uses a typical dashboard layout with the admin navigation. The navigation shows the count of new comments.
- Each screen with state has a pure `Model`, `Msg`, and `update`.

**The editor** (layout D, "Paper", from the prototype on `prototype/post-editor`):

- No site theme and no admin navigation. One column with the title, the excerpt, and the Markdown body.
- CodeMirror 6 through a hand-written binding, with a small `@lezer/markdown` extension that marks the directive lines. No line numbers and no gutter.
- The top bar: "Posts", the status text, "Preview", "Details", one primary action, and "•••". The bar fades while the author types.
- The status text shows the state, the save state, and the deploy state, with "retry" for a failed deploy.
- The "•••" menu: Save, Changes from live, Discard changes, Change the date, Unpublish, Revisions, Send to subscribers, and Delete. Each action that is not available shows the reason. On a phone, the menu also has "Add image".
- The Details sheet: the slug, the category, the tags, the series, the card image, the author, "Show on the home page", and the list of problems. On a phone, the sheet fills the screen.
- The publish dialog asks for "now" or a date. For "publish changes", it shows the changes from live and the option to set the update date. An error disables the publish.
- Autosave about 2 seconds after the author stops typing, and ⌘S saves at once.
- An image paste or drop starts the upload at once. A CodeMirror widget outside the Markdown text shows the placeholder. When the upload returns the URL, the editor writes `![](<url>)` and puts the cursor between the brackets. A PDF becomes a `::pdf{file="…"}` line. A failed upload removes the widget and shows a retry.
- A token expiry in a session keeps the editor text.

### Stages and the local stage

- `dotnet test` reads the fixture of the 10 posts and never reads the store. `scripts/verify.sh` needs no network.
- For the local stage, `pull` uses `DefaultAzureCredential` and the `az login` of the developer.
- The local backend writes the post store, the post table, and `assets` to Azurite. It logs each dispatch and does not send it, and it marks each change as deployed at that time. The build and the preview accept an http Azurite URL on the local stage only.
- An npm `dev` script in the admin project serves the admin on `http://localhost:4322`, with the same fallback and bridge page as the SWA config. The local admin signs in with the real Entra tenant and calls the local backend on `:7071`.

### CI and deploy

- `backend-staging.yml` gets an `admin` job after the backend job. It runs `preview-styles`, `npm ci`, `npm run build`, and `npm test` in the admin project, then `Azure/static-web-apps-deploy@v1` with a deploy token secret. A repo-name guard runs the job only in the `staging` repo. The `prod` repo builds no admin.
- `ci.yml` gets an `admin` job that builds and tests the admin bundle on each PR.

### Android retirement

- The Android app is already retired. The two admins never run together.
- The Android parts leave the backend in part 5: `Admin-Posts-Sync`, `Admin-Notifications-Register`, `SendPushNotification`, the Firebase initializer, the `FirebaseAdmin` package, the Firebase secret in Key Vault, the `Client` type, the `deviceRegistrations` table, the `android` client, and the setup document for Firebase. The Firebase setup document goes in the same PR that removes `FirebaseAdmin`.

### Manual setup

The repo has no infrastructure as code, and this work adds none. Each manual step is in a checklist or a `wizard` script:

- Step 2 wizard: the post store account, its four containers, the roles for the build identity and the developer, and the federated credentials.
- The backend identity: read and write on the post store containers, and write on `assets`.
- The GitHub App: Actions: write, the `workflow_run` webhook to the backend, the install on the two repos, its private key and the webhook secret in Key Vault.
- Entra: the "TwoPoint Admin Web" registration, its pre-authorization, v2.0 tokens on the API, and the delete of "TwoPoint Admin Android". The order of these steps is not important, because no client calls `internal/` until the admin is live.
- The SWA resource, its deploy token secret in the `staging` repo, and the `CNAME` record.
- The CORS origin on the Function App.
- The VAPID key pair, with the private key in Key Vault.
- After step 2 is live in production: delete the containers `posts`, `series`, and `thumbnails` on `twopointwebsite`.
- After the Android removal: delete the Firebase project and the `../TwoPointAdmin` folder.

## Testing Decisions

A good test checks the behavior that the author, a reader, or the build sees: the result of an action, the problems of a post, the bytes at a path, or the state after a message. A test does not check a private helper or the call order inside a module.

### Seams

1. **The library check** (a new library test project, in `dotnet test`). The stage, an image size port, the series records, the published posts, and one post give the article HTML and the problems. The series and term rules, the year tag, the alt text, the image outside `assets`, the Azurite URL, the link that breaks in another post, and the place of each problem test here. The tests of the moved modules move here from `Generator.Tests`.
2. **The render map** (`Generator.Tests`). The fixture folder of posts and series records gives the output map, with no disk and no download. The series pages from the records, the term title of the newest post, `file:line` in the build errors, the agreement of the site paths with the library function, `pull` behind a fake blob client, and the `upload` delete of a newsletter with no post test here.
3. **The backend actions** (`backend/Core.Tests`). Each action is a pure `Logic.decide` and a `withDependencies` with in-memory fakes, as `StartNewPostDelivery` is tested today. The post store, the tables, the dispatch, and the push stay behind the dependencies. These test here: the post lifecycle actions and the refusal on an error, the moderation actions and the trusted and banned rules, the series save, delete, and slug lock, the subscription cancel and delete, the dashboard counts, the deploy rule (run numbers, replaced runs, a failed dispatch), the delivery gate, the welcome post, and the `new_comment` handler. The HTTP endpoints stay thin and get no tests, as today.
4. **The Web Push sender** (`backend/Dependencies.Tests`). The encryption against the vectors in RFC 8291 Appendix A and RFC 8188, and the VAPID JWT.
5. **The pure admin code** (the admin test project, in `dotnet test`). Each screen `update`: the editor states, the status text, the menu reasons, the publish dialog, the sign-in banner, the autosave timer, the paste flow, the lists, the filters, and the search. Also the function of the worker that makes a notification from a payload.
6. **The built admin bundle** (`npm test` in the admin project, CI only). The bundle runs in happy-dom with a stub `fetch` and a stub sign-in port, so MSAL never runs in a test. For example: no API call before a token, and a save after 2 seconds with no input.
7. **The Comments widget**: the trusted reply in `Widgets.Tests` and in the widget bundle test.

### One-time checks

The result of each check goes in the PR body.

- The `diff -r` of the build output in each migration PR.
- Web Push from the hand-written sender in Chrome, Firefox, Safari on macOS, and a Home Screen app on iOS.
- The MSAL redirect, the popup in a session, and the 24-hour refresh limit.

### Prior art

- `backend/Core.Tests/StartNewPostDeliveryTests.fs`: `Logic.decide` and `withDependencies` with fakes.
- `frontend/Generator.Tests`: the render map tests, the fixture, and `PostsTests` with `astro-posts.json`.
- `frontend/Widgets.Tests` and the bundle tests in `frontend/Widgets/test/`: the pure `update` and the happy-dom bundle.

## Out of Scope

- A render at request time, or a go-live with no rebuild. The site stays static with a rebuild after a publish.
- A scheduled go-live. An upcoming post is on the site with its badge, as today.
- Page-view analytics.
- New work in the Android app.
- More than one admin, and admin roles.
- An edit of a comment, and an email to a commenter about moderation.
- An add, a reactivation, an edit, or an export of a subscription in the admin.
- An asset delete, an Assets page, and a custom domain for assets.
- A record or a page for a term.
- Infrastructure as code.
- The split of this spec into issues. `to-tickets` does it.

## Further Notes

### Decisions that this spec adds

No ticket decided these points. Each one follows from the tickets.

1. **Drafts count from the store.** "Choose the counts on the dashboard" counted the drafts in the post table. The post table gets a row only at a publish, so the count reads the `working` blobs with no `published` blob.
2. **The migrated mark is blob metadata** on the `published` blob. The ticket says that the migration uploads the posts with a mark. A manual upload between step 2 and the admin also sets it, so a post from that period does not count as a delivery to start.
3. **The series records are a fourth container, `series`**, with one YAML blob for each series. The build identity reads `series` and `published`. Step 2 makes the 2 series records, because step 2 sets the store format.
4. **Unpublish deletes the post-table row.** The post becomes a draft, and a draft has no row. An upcoming post has no comments yet.
5. **A date change updates the created date of the post-table row,** so the welcome and the delivery gate use the new date.
6. **Step 2 adds `workflow_dispatch` to `site.yml`,** because the manual runs between step 2 and the admin need it.
7. **A failed production run notification** opens the dashboard. Its text is for the implementation to choose.

### Paths in this spec

The spec names a path only where the map decided it as a contract: a public URL, a route, a container, a blob name, and a workflow file. Other paths are for the implementation to choose.
