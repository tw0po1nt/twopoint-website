Source: https://github.com/tw0po1nt/twopoint-website-staging/issues/124

## Problem Statement

twopoint.dev is an Astro site on the AstroWind template. The backend and the interactive widgets are F#, but the rest of the site is 77 `.astro` files, 14 `.ts` files, a Node upload script, a Tailwind config, and a Node build. The owner writes F#, and keeps a second language and a second toolchain only for the site shell.

The Astro build also has defects. Its links get a `301` on GitHub Pages, its sitemap lists 10 URLs that give `404`, one canonical URL gives `404`, `og:image:height` is wrong, `404.html` is indexable, `<time datetime>` is not ISO 8601, and the newsletters have broken links and an empty PDF block. The design is the stock AstroWind look.

## Solution

An F# program builds twopoint.dev as a static site, with no Astro and no hand-written TypeScript or JavaScript. Each public URL stays. GitHub Pages still hosts the site, and the production build still uploads each newsletter to Azure Blob.

The site gets the "Source+" design: a code-native look in light and dark, a post log on the home page, and one reading column with an outline, an explorer, and a Comments panel. The widgets move to Oxpecker.Solid. The F# build grows beside Astro, replaces Astro on staging when the posts and the home page render, and replaces Astro on prod in one release when it writes each URL. One PR then deletes Astro.

The map [Wayfinder: convert twopoint.dev to F#](https://github.com/tw0po1nt/twopoint-website-staging/issues/97) contains the decisions. Each ticket of the map contains the detail and the reasons for its decisions. When this spec and a ticket disagree, this spec is correct.

## User Stories

### Readers

1. As a reader, I want each old link to a post, a list, or a page to still work, so that bookmarks and links from other sites do not break.
2. As a reader, I want an old link to a heading to go to the same heading, so that a shared fragment link still works.
3. As a reader, I want the site to follow my system light or dark setting, so that the site agrees with my other apps.
4. As a reader, I want a theme button that keeps my choice across pages and visits, so that I do not set it again.
5. As a reader, I want no flash of the wrong theme when a page loads, so that the page does not blink.
6. As a reader, I want the site to work with no JavaScript, so that I can read each post in any browser.
7. As a reader, I want code blocks with colors in both themes, so that I can read the code.
8. As a reader, I want a long code line to scroll sideways, so that the F# indentation stays clear.
9. As a reader, I want a language label on each code block, so that I know the language of the code.
10. As a reader, I want a copy of a diff block to omit the `+` and `-` marks, so that I can paste the code.
11. As a keyboard user, I want to scroll a code block with the keyboard, so that I can read a wide block.
12. As a reader, I want an outline of the headings beside a post on a wide screen, so that I can go to a section.
13. As a reader, I want the date, the author, the series, the tags, and the reading time above a post, so that I know what the post is.
14. As a reader of a series post, I want to see its series and its position, so that I can read the series in order.
15. As a reader, I want links to the older and the newer post, so that I can read more posts.
16. As a reader, I want related posts at the end of a post, so that I can find posts on the same topic.
17. As a reader, I want share links for x, facebook, linkedin, whatsapp, and email, so that I can share a post.
18. As a reader, I want an explorer with each post by year, the series, and the tags, so that I can find a post from any page.
19. As a phone user, I want the explorer to open and close with one tap and with Esc, so that it does not block the page.
20. As a reader, I want a header that stays at the top of the page, so that the links stay near.
21. As a reader, I want a home page with the latest post, a post log by year, and the series, so that I can see what is new.
22. As a reader, I want a banner for an upcoming post on the home page, so that I know what comes next.
23. As a reader, I want list pages for each category, series, and tag, so that I can read posts on one topic.
24. As a reader, I want body images that do not move the text while they load, so that I do not lose my place.
25. As a reader, I want to see the slides of a talk on its post page, page by page, so that I can follow the talk.
26. As a reader, I want a download link for the slides that works before the viewer loads, so that I can get the PDF at once.
27. As a reader, I want embedded videos to play on the page, so that I can watch a talk.
28. As a reader, I want external links to open in a new tab, so that I keep my place in the post.
29. As a reader, I want a 404 page that looks like the site, so that I can find my way back.
30. As a reader, I want an RSS link and an RSS feed, so that I can follow the blog in a reader app.
31. As a reader, I want the site to send no font request to a third party, so that my visit stays private.
32. As a reader, I want a privacy policy that names each storage key and each embed, so that I know what the site stores.

### Commenters

33. As a commenter, I want to read and post comments in a panel after a post, so that I can discuss the post.
34. As a reader who does not open the panel, I want no comment request, so that the page loads less.
35. As a commenter, I want the site to remember my email validation, so that I do not validate each time.
36. As a commenter, I want the Comments panel to open on the publish date of an upcoming post, so that I can comment when the post is live.

### Subscriptions

37. As a reader, I want a subscribe form on the home page, in the explorer, and on `/subscribe/`, so that I can subscribe from any page.
38. As a subscriber, I want the unsubscribe link in an email to work and to give a clear result, so that I know the subscription is inactive.
39. As a subscriber, I want each newsletter to keep its look, so that the emails stay familiar.
40. As a subscriber, I want each link in a newsletter to work in my email client, so that I can open the post and the linked posts.
41. As a subscriber, I want a link to the slides and a thumbnail of each video in a newsletter, so that the email shows each embed.
42. As a subscriber, I want code blocks in a newsletter to stay dark and to wrap, as today, so that I can read them in each client.
43. As a new subscriber, I want the welcome email to contain the latest post, so that I get a first newsletter at once.

### The author

44. As the author, I want to write a post as Markdown with YAML front matter, so that I need no MDX or imports.
45. As the author, I want to embed a PDF or a video with one directive line, so that a post depends on no site component.
46. As the author, I want a link to another post as a plain root-relative link, so that the link is easy to write.
47. As the author, I want the build to fail on a broken internal link or anchor, with the file and the line, so that no post goes live with a broken link.
48. As the author, I want the build to fail on a bad directive, an unknown fence language, an unknown front matter field, or a `class` or `style` in raw HTML, so that each post stays valid.
49. As the author, I want the build to fail when an image download fails, with the post and the URL, so that no post goes live with a missing image.
50. As the author, I want each social card image cropped to 1.91:1 from the post or series image, so that each share shows a correct card.
51. As the author, I want a post with a future date to go on the site with comments closed until that date, so that I can publish before a talk.
52. As the author, I want a draft to stay out of the site, so that I can commit work in progress.
53. As the author, I want to see each change at once in a local preview, so that I can write and style quickly.
54. As the author, I want a preview of each newsletter in the local preview, so that I can check the email before a release.
55. As the author, I want a clear error page in the local preview when a post is invalid, so that I never see an old page as current.

### Search engines and social platforms

56. As a search engine, I want each canonical URL, sitemap entry, and RSS link to give a `200` with no redirect, so that I index the correct URL.
57. As a search engine, I want a sitemap that lists only indexable pages, so that I do not crawl a `404` or a duplicate.
58. As a search engine, I want `noindex` on staging, on `404.html`, and on `/category/series/`, so that I do not index duplicates.
59. As a social platform, I want the correct `og:image` size and a new image URL when the image changes, so that the card shows the current image.

### The developer and the agents

60. As the developer, I want one .NET SDK pin for the backend, the site, and CI, so that a preview SDK on my machine does not break the build.
61. As the developer, I want `scripts/verify.sh` to test the site with .NET only, with no Node and no network, so that the gate stays fast.
62. As the developer, I want each stage value in typed F# code, so that the compiler checks each stage.
63. As the developer, I want a build with no stage option to fail, so that CI never builds the wrong stage.
64. As the developer, I want `upload` to refuse a build that is not production, so that staging never replaces the production emails.
65. As the developer, I want one deploy workflow that sets the stage from the repo name, so that I disable no workflow by hand.
66. As the developer, I want CI to build the full site on each PR, so that a broken link or image fails the PR.
67. As the developer, I want a failed upload to stop the deploy, so that the feed never shows a post with no newsletter blob.
68. As the developer, I want a URL parity test, so that I know when the F# build writes each Astro URL.
69. As the developer, I want a check that the site CSS has a rule for each class, and no rule with no use, so that the CSS stays small.
70. As the developer, I want the pure widget logic tested in `dotnet test`, so that the widgets need no browser to test their state.
71. As the developer, I want each widget bundle tested in a headless DOM, so that a bundle error fails the build.
72. As the developer, I want the widget scripts to load only on the pages that use them, so that other pages load no widget code.
73. As the backend, I want the RSS shape, the slug in each link, and the blob name `posts/<slug>/index` to stay, so that post sync and the welcome email work with no change.

## Implementation Decisions

### Modules

The site is six .NET projects in a `site` solution folder, beside `frontend/` and `backend/`. The PR that deletes Astro renames the folder to `frontend/`.

- **Generator.** A compiled `net10.0` console program with two commands, `build` and `upload`. It contains the site model, the views, the Markdown pipeline, the highlighter, the image steps, the newsletter writer, and the upload. It embeds the grammar files and the scope map.
- **Generator tests.**
- **Widgets.** The Fable and Oxpecker.Solid widgets, the head script, the dev reload script, the widget contract, the HTTP client of the API, and the Node bundle build.
- **Widget tests.**
- **Dev.** The dev server. It references the Generator.
- **Dev tests.**

The content sits beside the projects: the posts, the privacy page, the assets (fonts, favicons, images), the site CSS files, and the two email CSS files. The site projects do not reference backend projects. Each side keeps its own copy of the contract types.

`TwoPoint.sln` becomes `TwoPoint.slnx`. A root `global.json` pins the .NET 10 SDK with `rollForward: latestFeature`. A root tool manifest pins the Fable CLI (5.21.0 or later) and SageFs, each at an exact version.

### The render interface

The center of the generator is one pure function. It takes the content, the stage, the build time, and the widget manifest, and gives a map from each output path to a deferred render (`unit -> bytes`).

- `build --stage local|staging|production` runs each entry and writes the files. The option is required and has no default.
- The dev server runs only the entry of the requested path.
- Each run is a full rebuild with no cache.
- `build` writes three outputs: the site folder for GitHub Pages, one newsletter file for each post, and a stage marker for `upload`. Only the site folder goes to GitHub Pages.
- The generator starts no other process.
- An error that comes from a post gives the post file and the line.

### Stages

A union of three stages, `Local`, `Staging`, and `Production`, with one record each. No configuration file contains stage values.

| Value | Local | Staging | Production |
| --- | --- | --- | --- |
| Site URL | `http://localhost:4321/` | `https://staging.twopoint.dev/` | `https://twopoint.dev/` |
| Site title | TwoPoint Local | TwoPoint Staging | TwoPoint |
| Description | (Local) A blog about programming, mostly functional | (Staging) A blog about programming, mostly functional | A blog about programming, mostly functional |
| `og:site_name` | TwoPoint (Local) | TwoPoint (Staging) | TwoPoint |
| `Indexable` | false | false | true |
| Pulse site id | none | none | the current id |
| API URL | `http://localhost:7071` | `https://func-twopoint-website-api.azurewebsites.net` | `https://func-twopoint-website-api.azurewebsites.net` |

A value that does not change by stage is a constant, for example the Google site verification id. The page title is `<page> — <site title>`. The base path is always `/`, and the build has no site or base option.

### URLs and output paths

- Each page is `<path>/index.html`. The one exception is `404.html` at the root.
- Each link, canonical URL, `og:url`, sitemap entry, RSS `link` and `guid`, and newsletter link has a trailing slash.
- Routes: `/`, `/posts/`, `/posts/<slug>/`, `/category/<category>/`, `/series/`, `/series/<series>/`, `/tag/<tag>/`, `/about/`, `/subscribe/`, `/unsubscribe/`, `/privacy/`, `404.html`, `/rss.xml`, `/sitemap-index.xml`, `/sitemap-0.xml`, and `/robots.txt`.
- Each list has 6 posts on a page. Page 1 has no number. Page 2 and later are `<base>/<n>/`.
- `/category/series/` stays, with `noindex,follow` and a canonical URL of `/series/`.
- `/decapcms/` and `/_headers` drop. `/decapcms/` is the one current URL that drops.
- Each file with a content hash in its name goes under `/_/`: the widget files under `/_/widgets/`, and the CSS, the fonts, the favicons, the Open Graph images, the default image, and the About photo directly under `/_/`.

### Posts

- A post is Markdown with YAML front matter and raw HTML. The slug is the file name.
- Fields: `publishDate` (UTC), `updateDate`, `draft`, `title`, `excerpt`, `author`, `category`, `tags`, `series` (`slug`, `title`, `excerpt`, `image`), `image`, and `showOnHomepage` (default `true`). The build fails on an unknown field.
- These fields go: `showHeroImage`, `metadata`, `series.next`, and `series.previous`.
- The build omits a draft. It includes an upcoming post in each list, page, feed, and sitemap.
- Category, tag, and series slugs and tag titles keep their current values. For example, `.NET` gives `net`, and the tag `fsharp` shows as `F#`.
- Reading time: `Math.ceil` of the minutes at 200 words a minute, on the whole post text, code included.
- Lists sort by `publishDate`, the newest first.
- Related posts: 5 points for the same category, 1 point for each shared tag, and the top 4 show.
- `<time datetime>` is ISO 8601 UTC.

### Markdown

- Each heading keeps the `id` that the Astro build gives it (the `github-slugger` rules).
- A leaf directive alone on its line, `::name{attr="…"}`, embeds a widget or a video. The node set is closed: `Pdf` and `YouTube`. The build fails on an unknown name, a missing or unknown attribute, or a bad value.
- `::pdf{file="…"}` gives a figure with a Pdf mount and a static download link on the site, and one link to the post in the newsletter.
- `::youtube{id="…"}` gives a `youtube-nocookie.com` iframe on the site, and the video thumbnail with a link in the newsletter.
- An internal link is a root-relative Markdown link. The build fails when its path or anchor is not in the output. The build fails on a link to `https://twopoint.dev/`.
- The site adds `target="_blank" rel="noopener noreferrer"` to each external link.
- The newsletter makes each root-relative `href` and `src` absolute with the site URL of the stage.
- Each body image is a Markdown image with `loading="lazy"`, `width`, and `height`.
- The build fails on a `class` or `style` attribute in the raw HTML of a post.

### Code highlighting

- TextMateSharp tokenizes each code block at build time, with pinned copies of the Shiki grammars from `tm-grammars` 1.32.22 for F#, C#, Swift, sh, diff, and JSON. The XML and CSS grammars come from TextMateSharp.Grammars. A sources file gives each version and license.
- A pinned copy of the Shiki `css-variables` scope map gives each token a role from a closed set.
- A token is `<span class="tk-<role>">`. Default text gets no span. No `style` attribute.
- A block is `<pre data-lang="<fence>" tabindex="0"><code>…</code></pre>`. The CSS shows the label. A block with no language has no label and no colors.
- A diff mark is `<span class="tk-diff-mark">`, with `user-select: none`.
- The fence languages are a closed set with no aliases: `fsharp`, `csharp`, `swift`, `sh`, `diff`, `json`, `xml`, and `css`. The build fails on another name.
- In the newsletter, the email CSS gives each role a `github-dark` color, and the `pre` the `github-dark` background and `pre-wrap`.

### Pages and design

- The design is variant E, "Source+", of the prototype on the branch `prototype/design-directions`. The Support panel of the prototype goes.
- Each page head has the title, the description, the canonical URL, the robots meta, the Open Graph tags, the RSS `alternate` link, the Google verification meta, the favicons, the site CSS, the inline head script, and the Pulse script in production only.
- The header has the logo, the links (`posts`, `one-offs`, `series`, `talks`, `subscribe`, `about`), the theme button, and the `[explorer]` button. It uses `position: sticky`.
- The explorer is an `<aside popover>` with each post by year, the series, the tags, a Subscribe mount, and the footer links (`/privacy`, GitHub, `rss.xml`).
- The post page has the path line, the front-matter block, the title, the excerpt, the body, the outline of the `h2` and `h4` headings, the share links, the older and newer cards by publish date, the Comments panel, and the related posts.
- The home page has the intro, the latest post card (the newest post that does not set `showOnHomepage: false`), the post log by year, the series, a Subscribe mount, and the upcoming post banner.
- Share links are plain `<a href>` links with the share URL. Each one opens in a new tab, except email.
- No client router, no view transitions, no scroll animations, no icon library, and no HTML minify.

### SEO, feed, and sitemap

- The canonical URL of a post comes from its slug.
- The robots meta is `index,follow`, or `noindex,follow` for `/category/series/` and `404.html`. When `Indexable` is false, each page gets `noindex,nofollow`.
- The sitemap lists each page with `index`, and no other page. The staging sitemap lists the same pages as the production sitemap.
- `robots.txt` has `User-agent: *` and an empty `Disallow:`. When `Indexable` is true, it also has the `Sitemap` line.
- The RSS feed keeps its shape: the channel title `TwoPoint’s Blog`, and for each post `title`, `link`, `guid`, `description`, and `pubDate`.
- `404.html` has no canonical URL and no `og:url`, and each of its paths starts at the root.

### Images

- `og:image` is the only use of `series.image` and `image`, in that order, with the site default last.
- The build downloads each source and writes a 1200×630 center crop with SixLabors.ImageSharp as `/_/og-<hash>.jpg`, with no upscale. `og:image:width` and `og:image:height` come from the written file.
- A failed download after a small number of retries fails the build. No host list.
- The default image and the About photo are committed copies that the build copies with a hash. The 18.5 MB `default.png` goes.
- Body images keep their Blob URL. The build reads their size.
- WebP copies, `srcset`, `unpic`, and the unused store badges go.

### Styles

- Hand-written plain CSS in about five files. The generator joins them in a listed order and hashes the result. A file that is not on the list fails the build. No CSS tool. Tailwind goes.
- Semantic class names. The post body uses element selectors under one container.
- Each color uses `light-dark()`. `data-theme` on `<html>` forces one theme.
- The site CSS contains the widget styles.
- Self-hosted Latin-subset WOFF2 fonts: JetBrains Mono and IBM Plex Sans, with their OFL license. Inter goes.
- Baseline "newly available" features by the end of 2024. No vendor prefixes.
- The email CSS stays apart from the site CSS.

### Widgets and the head script

- Oxpecker.Solid 1.1.0 on solid-js 1.9, with Fable 5 stable. React, Feliz, and `vite-plugin-fable` go. No Datastar.
- The widgets: Subscribe, Comments (with NewComment), Unsubscribe, and Pdf. The Support widget goes, with its toast and QR code packages.
- Each widget with state has a pure `Model`, `Msg`, and `update`, which a small helper runs on a Solid signal.
- The contract: the generator writes `<div data-widget="<name>" data-<prop>="…">`. One plain F# file contains a closed union of the widgets with their props, `toAttributes`, and `parse`. The Generator and the Widgets both compile it. The generator writes `data-api` from the stage.
- The generator adds a widget script and its preloads only to a page with a mount for that widget. A bad mount logs an error and keeps its static HTML.
- The bundle build: `dotnet fable` writes JSX, and one Node script bundles it with esbuild and an inline Babel step for Solid. One entry for each widget, with shared chunks, minified, target `es2022`. A manifest gives the entries with their imports, the head script, and the dev reload script. The build fails when a widget has no entry.
- Comments loads its view on the first `toggle` of its panel. Pdf imports `pdfjs-dist`, and the build gives it the hashed worker URL.
- Unsubscribe reads `subscriptionId`, sends the cancellation call, and shows one of four results.
- The head script is a classic IIFE of at most 2 KB with no `fable-library` code. The generator writes it inline in each head. It applies the stored theme, handles each `[data-theme-toggle]` click, and runs the upcoming post check. It keeps the `localStorage` key `theme`.
- Upcoming post: the generator writes the banner and hides the Comments panel for a post after the build time. After the date, the head script shows the panel and hides the banner.
- Comments keeps the `localStorage` key `commenterValidations`.

### Newsletter and upload

- The newsletter view uses the same post model and keeps the current email look, slots, and Google Fonts link.
- PreMailer.Net 2.7.4 inlines the email inline CSS. The email embed CSS is the one `<style>` block, and the inliner ignores it and the font link.
- `upload --container <uri>` sends each newsletter as the blob `posts/<slug>/index`, with `text/html` and `public, max-age=3600`, through `DefaultAzureCredential`. It makes the container if it does not exist.
- `upload` stops if the stage marker is not production. It has no stage option and runs no build.

### Dev loop

- SageFs is the loop. A committed SageFs init script starts the dev server, so a save of an F# view patches the running server in place.
- The dev server listens on port 4321 and renders each request from the render map. It writes nothing to the output folder.
- It watches the content, the CSS, the assets, the email CSS, and the widget output, then reloads the model and sends a reload event over SSE to a small Fable reload script.
- A failed model reload gives a `500` page with each error.
- `/__dev/` links to the newsletter of each post.
- The init script starts the widget watch, with source maps.
- Only the dev server caches images, on disk and in memory.
- The backend runs apart, with `func start`.
- When work finds a SageFs defect, draft an upstream issue with a minimal repro. The owner confirms before it is filed, and decides what to do next.

### CI and deploy

- A composite action takes a stage and runs: the .NET setup from `global.json`, the Node setup from the widget `.node-version`, `dotnet tool restore`, `npm ci`, `npm run build`, `npm test`, and `build --stage <stage>`.
- `ci.yml` keeps the `verify` job and adds a `site` job with `staging`.
- `site.yml` sets the stage from the repo name: the prod repo is `production`, the staging repo is `staging`, and another repo stops. On production, it runs `upload` with the `NEWSLETTER_CONTAINER_URI` secret and no `continue-on-error`. Then it deploys the site folder to Pages.
- `scripts/verify.sh` keeps three steps with `TwoPoint.slnx`, and needs .NET only.
- Before the staging switch, CI builds the F# site and nothing deploys it, and each post edit changes both copies.
- The staging switch adds `site.yml` and deletes the Astro staging workflow. After it, a post change goes only to the new content.
- No release goes to prod until the cutover.

### Cutover

1. The last page PR merges, the pending list is empty, and the staging deploy is green.
2. The owner does the browser checklist on staging in desktop Chrome, Safari, and Firefox, and in iOS Safari.
3. The owner does the newsletter check with three posts in Gmail, iCloud, and Outlook.com.
4. The owner adds the `NEWSLETTER_CONTAINER_URI` secret to the prod repo.
5. A staging PR deletes the Astro prod workflow.
6. The release PR goes to prod. It runs the production build, the first upload, and the Pages deploy.
7. The prod checks: a `curl` of each path of the URL list, `Admin-Posts-Sync`, `verify.sh`, and a test subscription.

No rollback: a defect after the release gets a fix and a new release. Then the delete PR removes Astro, the `.mdx` copies, the Node upload script, the two YAML config files, the post text script, the URL list, the pending list, and the parity test. It renames the site folder to `frontend/`. The owner removes the two old storage secrets from both repos.

## Testing Decisions

A good test checks the behavior that a reader, the author, or the backend sees: the bytes at a path, the state after a message, or the DOM after a bundle runs. A test does not check a private helper or the call order inside a module.

### Seams

1. **The render map** (Generator tests and Dev tests). Content, a stage, a build time, and a manifest give the output map. Tests read the paths and the bytes, with no disk and no download. The pages, the feed, the sitemap, `robots.txt`, the newsletters, the link check, the directive errors, the heading ids, the highlighter output, the class check, and the URL parity test all test here. The image fetch and the blob client each sit behind one I/O interface, which tests replace with a fake.
2. **The pure widget code** (Widget tests, in `dotnet test`). Each widget `update`, each contract `parse` with its `toAttributes` round trip, and the theme and date logic of the head script.
3. **The built bundles** (`npm test` in the widget build). Each built entry runs in happy-dom with a mount and a stub `fetch`. The Comments test checks that no fetch occurs before `toggle` and one fetch occurs after it. CI runs it, and `verify.sh` does not.

### Named checks

- **URL list and pending list.** The first generator issue builds Astro one time and commits the list of its public paths: 44 pages (with `404.html`) and the 4 feed and robots files. It also commits a pending list. The parity test fails when a listed path is missing and not pending, or when a pending path is in the output. Each page issue removes its paths from the pending list.
- **Class check.** Each class in the output has a CSS rule, and each rule has a use. Widget classes are on an allow list. An unused `tk-*` rule passes. The newsletter output checks against the email CSS.
- **Newsletter contract.** One fixture post through the email path: the slots stay, no script, no site stylesheet, the embed style block as written, and the inline styles on a code token and a body image.
- **Tests on the real posts.** The tests load the real content, so `verify.sh` fails on an invalid post.
- **One-time checks**, with the result in the PR body: the highlighter roles against Shiki on each current code block, the link text and targets before and after the post conversion, and the post text of both builds at the staging switch.

### Prior art

- `backend/` test projects: the xUnit layout and the pure-function style.
- The current widget HTTP client, which has no React code and can move as it is.
- The prototype on `prototype/design-directions` for the markup and the CSS of the home page and the post page.

## Out of Scope

- Backend changes, except a contract change that the new build forces. The current plan forces none.
- New site features, such as search.
- A newsletter email redesign. Emails keep their look.
- Edits to post wording. Posts change format only.
- A change of hosting.
- The Support widget. It goes.

## Further Notes

### Decisions that this spec adds

No ticket decided these points. Each one follows from the tickets or from the inventory.

1. **Output folders.** `build` writes the site and the newsletters to separate folders, and only the site folder goes to GitHub Pages. "Choose the CI and deploy changes" gave the whole output folder to Pages, which would publish the newsletters and the stage marker.
2. **Heading ids** keep the Astro values, as the inventory marks them Keep.
3. **Older and newer cards** link by publish date, as the prototype shows. `series.previous` and `series.next` go, and the series position comes from the date order.
4. **The staging switch is its own issue**, after the post page and the home page. "Choose the CI and deploy changes" put it in the PR that first makes them. The rule is the same.
5. **No HTML minify.** GitHub Pages compresses each file.
6. **`showOnHomepage` stays** for the latest card. `metadata` goes with its one key.
7. **Privacy policy.** The `theme` and `commenterValidations` keys keep their names. The policy gets one paragraph for the `youtube-nocookie.com` embed, which the owner approves.
8. **Pages that the prototype does not show** use its parts. The PR proposes the layout, and the owner reviews it.

### Paths in this spec

The spec names a path only where the map decided it as a contract: a public URL, the `/_/` prefix, the blob name, the solution file, and the stage values. Other paths are for the implementation to choose.

