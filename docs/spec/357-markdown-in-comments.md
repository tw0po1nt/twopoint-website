Source: https://github.com/tw0po1nt/twopoint-website-staging/issues/357

Map: [Wayfinder: Markdown in comments and comment replies](https://github.com/tw0po1nt/twopoint-website-staging/issues/339). The replies spec comes after this spec and assumes that a reply is Markdown like a comment.

## Problem Statement

A commenter on twopoint.dev can write only plain text. Readers of a technical blog want to show code, to link to a source, and to quote the post. Today a comment shows in one paragraph with its line breaks, so a code sample loses its shape, a URL is not a link, and a quote looks like the commenter's own words. The commenter cannot see how the comment will look before they post it.

The author has the same problem in the admin. The moderation queue, the comments of a post, and the commenter page show the plain text, so the author cannot see the target of a link before an approval.

## Solution

A comment is Markdown. A commenter can write emphasis, strong, inline code, links, code blocks, lists, and block quotes. Readers see the rendered result in the Comments widget. Other syntax stays as the commenter typed it. The backend renders the Markdown source of each comment at each read, and gives only HTML to the site and the admin. The comment table keeps only the source, so each existing comment also renders as Markdown, with no migration.

The comment form gets `[write]` and `[preview]` tabs and a short Markdown help. The preview tab shows the HTML from the backend, with one call at each open of the tab and no call at a keystroke. A code block with a language of a post gets the highlighting of a post. In the admin, each link shows its URL after its text. The new-comment notification shows the comment as plain text.

## User Stories

1. As a commenter, I want to write `*text*` and `**text**`, so that I can stress a word.
2. As a commenter, I want to write inline code with backticks, so that a type or a function name shows as code.
3. As a commenter, I want to write a fenced code block, so that a code sample keeps its indent and its lines.
4. As a commenter, I want to write an indented code block, so that code that I paste with four spaces also shows as code.
5. As a commenter, I want a fence with a language of a post (`fsharp`, `csharp`, `swift`, `sh`, `diff`, `json`, `xml`, or `css`) to get the colors and the label of a post, so that my code reads as the code of the post.
6. As a commenter, I want a fence with another language to show as plain code, so that my comment is accepted with any info string.
7. As a commenter, I want to write `[text](https://…)`, so that I can link to a source.
8. As a commenter, I want a bare `https://` URL to become a link, so that I do not have to write the link syntax.
9. As a commenter, I want to write `mailto:` links, so that I can give an email address as a link.
10. As a commenter, I want to write bulleted and numbered lists, so that I can list steps or points.
11. As a commenter, I want to write a block quote with `>`, so that I can quote the post or another comment.
12. As a commenter, I want one new line to be a line break, so that my lines show as I typed them, as today.
13. As a commenter, I want a blank line to start a new paragraph, so that a long comment has paragraphs.
14. As a commenter, I want `# Title`, `---`, and `| a |` to stay as I typed them, so that a heading or a table does not break the layout of the comments.
15. As a commenter, I want an image to become a link with its alt text, so that I can still point readers to the image.
16. As a commenter, I want the limit of 1,000 characters to count the text that I type, so that the counter of the form and the backend agree.
17. As a commenter, I want a `[preview]` tab next to `[write]` on the label row, so that I can see the rendered comment before I post it.
18. As a commenter, I want the preview to replace the text box with the same height, so that the form does not move when I change the tab.
19. As a commenter, I want the preview to use the style of a comment in the list, so that it shows what readers will see.
20. As a commenter, I want an empty text to show "Nothing to preview. Write a comment first.", so that I know why the tab is empty.
21. As a commenter, I want "Loading the preview…" during the call, so that I know the site is working.
22. As a commenter, I want a failed preview to say that I can still post, with `[try again]`, so that a preview error does not stop my comment.
23. As a commenter, I want to post my comment from each tab, so that I do not have to go back to `[write]`.
24. As a commenter, I want the preview to work before I verify my email address, so that I can check my comment first.
25. As a commenter, I want a `[markdown help]` control under the field, so that I can learn the syntax that works.
26. As a commenter, I want the help to show a "you type" and "readers see" table, so that I can copy the syntax.
27. As a commenter, I want the help to say that headings, images, and tables stay as typed, so that I am not surprised.
28. As a reader, I want comments with code, links, lists, and quotes to show as formatted text, so that comments are easy to read.
29. As a reader, I want an external link of a comment to open in a new tab, so that I keep my place in the post.
30. As a reader, I want a link of a comment to twopoint.dev to open in the same tab, as a link of a post does.
31. As a reader, I want a `javascript:`, `data:`, or relative link of a comment to show only as text, so that a comment cannot run code or send me to an unexpected place.
32. As a reader, I want no raw HTML from a comment on the page, so that a comment cannot change the page.
33. As a reader, I want the existing comments to show the same or better, so that the change does not break old comments.
34. As the author, I want each external link of a comment to have `rel="nofollow ugc"`, so that search engines do not count a comment link as mine.
35. As the author, I want the moderation queue to show the rendered comment, so that I moderate what readers will see.
36. As the author, I want the comments of a post and the commenter page to show the rendered comment, so that each admin page agrees with the site.
37. As the author, I want the URL of each link to show after its text in the admin, so that I can check the target on a phone with no hover.
38. As the author, I want code blocks in the admin with no colors, so that the admin stylesheet stays small.
39. As the author, I want the new-comment notification to show plain text with no Markdown marks, so that the notification is easy to read.
40. As the author, I want a change to a render rule to apply to all comments at the next read, so that I never migrate stored HTML.
41. As the author, I want the backend to refuse a comment of more than 1,000 characters, so that a request outside the form cannot post a longer comment.
42. As the author, I want a slow code block of a comment to stop at a short time limit, so that one comment cannot slow each render of the backend.
43. As the author, I want the preview endpoint to refuse a text of more than 1,000 characters and to store nothing, so that the endpoint stays cheap.
44. As the author, I want the preview to make one call at each open of the tab and no call at a keystroke or on a timer, so that the backend on Flex Consumption gets few calls.

## Implementation Decisions

**Comment renderer (new, in the Render library).** A new module next to the post body renderer, with two pure functions:

- `html`: the Markdown source of a comment to HTML.
- `plainText`: the same source to plain text for the notification.

The renderer uses its own Markdig pipeline:

- **Subset.** Emphasis, strong, inline code, links, autolinks, fenced and indented code blocks, lists, and block quotes. The pipeline turns off headings, thematic breaks, HTML blocks, and inline HTML, so that syntax stays as text. It adds no table, strikethrough, task-list, YAML, directive, or SmartyPants extension.
- **Images.** An image becomes a link with the alt text as its label.
- **Line breaks.** A soft line break renders as `<br>`. A blank line starts a new paragraph.
- **Raw HTML.** Raw HTML renders as escaped text.
- **URL allow list.** It is a walk of the syntax tree. Only an absolute `http`, `https`, or `mailto` URL makes a link. Each other URL (`javascript:`, `data:`, relative, fragment-only) renders the link text as plain text with no `href`.
- **Bare URLs.** A bare URL that starts with `http://` or `https://` becomes a link. A `www.` host with no scheme and a bare email address stay as text.
- **Link attributes.** An external `http` or `https` link gets `target="_blank"` and `rel="nofollow ugc noopener noreferrer"`. A link to twopoint.dev opens in the same tab with no `rel`. A `mailto` link has no `target`. The renderer uses the own-host rule of a post, with the stage of the backend.
- **Code blocks.** A fence with a language of a post goes through the code-block renderer of a post, with `data-lang` and the `tk-*` spans. Each other fence, and each indented block, renders as plain code with no `data-lang` and no class. Readers never see the info string of the commenter.
- **Plain text.** The `plainText` function keeps the text of each link, deletes the syntax marks, and changes each line break to a space.

**Highlight time limit.** The highlighter gets a time limit for each block, for comments only. The value is 50 ms. When the tokenizer stops early on a line, the whole block renders as plain code with no label. A post keeps no time limit.

**Render at each read.** The comment table keeps only the Markdown source, as today. A comment gets no format field, and the table gets no migration. Each comment answer of the backend gives `Content` as the rendered HTML, never as the source. This applies to:

- `GET blog/posts/{slug}/comments`, the public comments endpoint.
- `GET internal/comments`, the moderation queue.
- `GET internal/posts/{slug}/comments`, the comments of a post.
- `GET internal/commenters/{validationId}`, the commenter page.
- The answer of `Blog-Posts-PostComment`.

The JSON shape of each answer does not change, so the widget and the admin contracts keep their fields.

**Length limit.** The post-comment action refuses a source of more than 1,000 characters, counted as typed, with `400` and a message. The form keeps `maxlength = 1000` on the same source.

**Notification.** The new-comment payload uses `plainText` of the source before it truncates to 120 characters. The worker does not change.

**Preview endpoint (new).** `POST blog/comments/preview` is anonymous:

- **Request.** `{ "content": "<source>" }`.
- **Answer.** The `ApiResponse` envelope with the HTML of `html` as its data.
- **Refusals.** An empty source or a source of more than 1,000 characters gets `400`.
- **No state.** The endpoint stores nothing and needs no verified commenter.

**Comments widget.**

- **Comment text.** The widget puts the HTML of each comment into the comment element as inner HTML. The element changes from a `p` to a `div`, because the HTML has its own paragraphs. The stylesheet drops `white-space: pre-line` from `.comment-text`.
- **Tabs.** The comment field gets two tabs, `[write]` and `[preview]`, on the label row (`comment> [write] [preview]`). The selected tab has the text color and an underline, and the other tab is muted. The preview replaces the text box and keeps its height. `[post comment]` works from each tab.
- **Preview model.** It came from the prototype. The widget keeps the last source and its HTML. At each open of `[preview]`, the widget does one of these:
  - It shows "Nothing to preview. Write a comment first." when the text is empty.
  - It shows the kept HTML when the text did not change.
  - It calls the preview endpoint one time in the other cases.

  The preview has the states Empty, Loading ("Loading the preview…"), Failed ("The site cannot show the preview now. You can still post your comment." in the accent color, with `[try again]`), and Shown. The widget keeps no failed call. A late answer that is for an older source or an older open does not replace the newer state. The widget makes no call at a keystroke and no timer call.
- **Help.** One muted line under the field says that Markdown works and ends with `[markdown help]`. That control opens a static table in place, with two columns, "you type" and "readers see". The rows are italic, bold, inline code, a link, a list, a quote, and a code block. A line under the table says that one new line is a line break, and that headings, images, and tables stay as typed. The help makes no call.
- **Style.** A comment in the list and the preview use the same rules: links in the accent color, inline code on the second background, a code block between two lines, a block quote with the green left border, and lists with an indent. The post page already loads the `tk-*` colors.

**Admin.**

- **Comment text.** The queue row, the post comment row, and the commenter comment row put the HTML of the comment into the text element as inner HTML.
- **Link URLs.** The admin stylesheet shows the `href` after the text of each link in comment text, for example with `a::after { content: " (" attr(href) ")" }`.
- **Code.** The admin adds no `tk-*` rules, so code blocks show in monospace with no colors.

**Prototype.** The branch `prototype/comment-form` has the layout of the form and its help (variant A). The branch is not for merge.

## Testing Decisions

A good test checks the external behavior of a seam: the HTML or the plain text that comes out for a source, the answer of an action for a request, or the next model of the widget for a message. It does not check the Markdig pipeline setup or the private helpers.

The spec uses three seams.

1. **Comment renderer** (Render.Tests, as `BodyTests` and `HighlightTests`). Test each rule of the subset with one source and its expected HTML:
   - Each allowed element.
   - Each element that stays as text (heading, rule, table, raw HTML, strikethrough).
   - An image that becomes a link.
   - Each URL kind of the allow list, including `javascript:`, `data:`, relative, and fragment URLs.
   - Bare URLs, `www.` hosts, and bare email addresses.
   - The `target` and `rel` of an external, an own-host, and a `mailto` link.
   - A known fence, an unknown fence, and an indented block.
   - One newline and a blank line.
   - `plainText` for links, marks, and line breaks.
   - The time limit, with a grammar input that makes the tokenizer stop early, or with a time limit of zero through a test parameter, so that the block falls back to plain code.

   The 3 live comments of 2026-11-01 are fixture cases: the `record struct` code, the blank line and the bare URL, and the comment with no syntax.
2. **Core actions and queries** (Core.Tests, as `ModerationTests` and `NewCommentTests`, with fake dependencies). Test these:
   - The comments of a post give the rendered HTML in `Content`, both public and admin.
   - The moderation queue and the commenter page give the rendered HTML.
   - The post-comment action refuses 1,001 characters and accepts 1,000 characters.
   - The preview action gives the HTML, and refuses an empty source and a source of more than 1,000 characters.
   - The new-comment payload gets the plain text, truncated to 120 characters.
3. **Comments widget** (Widgets.Tests, as `CommentsTests`, through the Elmish `update`). Test these:
   - The tab changes.
   - The empty text.
   - One preview effect at the first open.
   - The reuse of the kept HTML when the text did not change.
   - A new call after an edit.
   - A failed call that is not kept, and `[try again]`.
   - A late answer after a return to `[write]` or a second open, which does not replace the newer state.
   - Posting from the preview tab.

The admin gets no new seam. Its change is a stylesheet rule and inner HTML in three rows. The `site` CI job and the `npm test` happy-dom run of the widget entries catch a broken mount.

## Out of Scope

- Replies to a comment. The replies spec comes next on the same map.
- Headings, images, tables, horizontal rules, raw HTML, strikethrough, and task lists in a comment.
- A live preview at each keystroke, a syntax toolbar, and a review step before each post.
- An edit of a posted comment.
- A rate limit on the preview endpoint.
- Colors of code blocks in the admin.
- A format field on a comment, and a migration of stored comments.

## Further Notes

Decisions on the map:

- [Survey Markdown renderers and HTML sanitizers for comment text](https://github.com/tw0po1nt/twopoint-website-staging/issues/340)
- [Choose the Markdown subset of a comment](https://github.com/tw0po1nt/twopoint-website-staging/issues/341)
- [Choose where the Markdown of a comment is rendered](https://github.com/tw0po1nt/twopoint-website-staging/issues/342)
- [Decide how the existing comments read as Markdown](https://github.com/tw0po1nt/twopoint-website-staging/issues/343)
- [Prototype the comment form with Markdown help and a preview](https://github.com/tw0po1nt/twopoint-website-staging/issues/344)
- [Choose whether a code block of a comment gets highlighting](https://github.com/tw0po1nt/twopoint-website-staging/issues/350)

The highlighting decision replaces the `language-<name>` class of point 6 of the subset decision. A fence with an unknown language has no class.

The survey found that Markdig with HTML turned off and a syntax-tree URL allow list blocks each bad case, with no new package. The widget thus needs no sanitizer, because the backend gives only HTML that it made.

The glossary does not change. A **Comment** is still the text that a commenter posts on one post.

