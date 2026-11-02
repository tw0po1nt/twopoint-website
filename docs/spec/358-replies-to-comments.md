Source: https://github.com/tw0po1nt/twopoint-website-staging/issues/358

Map: [Wayfinder: Markdown in comments and comment replies](https://github.com/tw0po1nt/twopoint-website-staging/issues/339). This spec comes after [Feature: Markdown in comments](https://github.com/tw0po1nt/twopoint-website-staging/issues/357) and assumes it: a reply is Markdown like a comment, with the same renderer, preview endpoint, and length limit.

## Problem Statement

A comment on twopoint.dev cannot get an answer. When a reader asks a question in a comment, the author can answer only with an edit of the post or a private email. Another reader who knows the answer cannot write it under the question. The commenter does not know when someone answers, so a question and its answer stay apart.

## Solution

A comment can get replies. A **thread** is a **top-level comment** and its **replies**, one level deep. A commenter replies on the site to an approved top-level comment. The author replies from the admin, on each row of the moderation queue and of the comments of a post. A reply follows each rule of a comment: the same Markdown, the same length limit, and the same moderation. A reply from the author is approved when the author posts it, and the widget shows it with the post author name and an `[author]` badge.

When a commenter posts a top-level comment, they can check "email me when someone replies". Then each approved reply from someone else sends them one **reply email**, with a link that stops the reply emails of that comment.

## User Stories

**Threads on the site**

1. As a reader, I want each reply to show under its top-level comment, so that I can read a question and its answers together.
2. As a reader, I want the threads in the order of their top-level comments, oldest first, so that a new reply does not move a thread.
3. As a reader, I want the replies of a thread oldest first, so that I can read the thread in order.
4. As a reader, I want replies with a small indent and a left line, so that I can see where a thread starts and ends on a phone.
5. As a reader, I want a thread with more than 3 replies to show the 2 newest and a `[show N earlier replies]` control, so that a long thread does not fill the page.
6. As a reader, I want a reply from the author to show the post author name, an `[author]` badge, and a thicker left line in the badge color, so that I can find the answer of the author.
7. As a reader, I want to see a reply only when the reply and its top-level comment are approved, so that I never see a reply to a hidden comment.
8. As a reader, I want a thread to show again when the author approves its top-level comment again, so that a reject is not a delete.

**Replies from a commenter**

9. As a reader, I want one `[reply]` control at the end of each thread, so that my reply goes where it will show.
10. As a reader, I want `[reply]` to open the reply form in place of the control, with a "reply to <name>" line, so that I know which comment I answer.
11. As a reader, I want the reply form to have the `[write]` and `[preview]` tabs and the help line of the comment form, so that I can format and check my reply.
12. As a reader, I want `[post reply]` and `[cancel]` in the reply form, so that I can post or close it.
13. As a verified reader, I want the reply form to show one line `as <name> (<email>) [change]`, so that I do not type my name and email again.
14. As a new reader, I want the reply form to show the name and email fields, so that I can verify my email address from a reply.
15. As a reader, I want the reply form and the page form to share my name and email, so that I type them one time.
16. As a reader, I want only one reply form open at a time, with the text of a closed form kept in memory, so that I do not lose a draft when I open another thread.
17. As a new reader, I want the verify link to bring me back to the reply form of my thread with my text, so that I can post the reply after I verify.
18. As a new reader, I want "Your email address is verified. Select [post reply] to post your reply." after the return, so that I know the next step.
19. As a trusted commenter, I want my reply to show at the end of the thread at once, so that I see my reply posted.
20. As a new commenter, I want "Your reply is posted. It shows here after the author approves it." in the thread, so that I know my reply waits for moderation.
21. As a commenter, I want my reply to have the Markdown subset and the 1,000-character limit of a comment, so that each comment rule is the same.
22. As a commenter, I want a reply to a comment that is no longer approved to be refused with a message, so that I know why my reply did not post.
23. As a commenter, I want to answer another reply by a name or a block quote in my text, so that I can answer a reply with one level of threads.

**Replies from the author**

24. As the author, I want a `[reply]` action on each row of the moderation queue and of the comments of a post, so that I can answer where I read a comment.
25. As the author, I want `[reply]` on a reply row to answer the top-level comment of that thread, so that the thread stays one level deep.
26. As the author, I want the reply form to open in line under the comment, with `[write]` and `[preview]` tabs and the same preview endpoint, so that I can check my reply.
27. As the author, I want the admin to show the URL of each link in my preview, as in each comment, so that I can check my links.
28. As the author, I want my reply approved when I post it, with no queue row and no notification, so that I do not moderate my own reply.
29. As the author, I want my reply to a new comment to approve that comment, and also its top-level comment if that comment is new, so that my answer is never under a hidden comment.
30. As the author, I want the reply action in the queue to tell me that it approves the comment, so that the approval is not a surprise.
31. As the author, I want no reply action on a rejected comment, so that I do not answer a comment that readers cannot see.
32. As the author, I want no reply action on the commenter page, so that I reply in the context of the post.
33. As the author, I want to delete my reply and write a new one, so that I can fix a reply with no edit action.
34. As the author, I want the reply form to show "<name> gets an email with this reply." when the commenter of the top-level comment gets reply emails, so that I know my reply goes to their inbox.
35. As the author, I want to write my reply with no commenter record and no email verification, so that I am never in the commenter list and never banned.

**Moderation**

36. As the author, I want a reply from a new commenter to go in the moderation queue, and a reply from a trusted commenter to be approved at the post, so that a reply follows the rule of a comment.
37. As the author, I want the blog to keep no reply from a banned commenter, so that a ban applies to replies too.
38. As the author, I want a reply row in the queue to show a "reply to <name>" line and a short excerpt of the top-level comment, so that I moderate the reply in context.
39. As the author, I want to open the full text of the top-level comment from the excerpt, so that I can read the whole question.
40. As the author, I want the reply row to tell me when the top-level comment is rejected, so that I know readers will not see the reply.
41. As the author, I want the queue to stay oldest first, with no group by thread, so that I moderate in order.
42. As the author, I want a reject of a top-level comment to change no reply, so that a later approval brings the thread back as before.
43. As the author, I want a delete of a top-level comment to remove its thread, with the number of replies in the confirm dialog, so that I know what goes.
44. As the author, I want a delete of a commenter to remove each thread that the commenter starts, with the number of replies from other writers in the confirm dialog, so that I know what goes.
45. As the author, I want a reply of a deleted commenter in another thread to go alone, so that the threads of other commenters stay.
46. As the author, I want the comments of a post to show each thread, with the replies indented and each status shown, so that I see the threads as readers see them.
47. As the author, I want the commenter page to stay a flat list, with the "reply to <name>" line and the excerpt on each reply, so that I see each comment of one commenter.
48. As the author, I want the new-comment notification of a reply to say "<name> replied to <name of the top-level comment>", so that I know it is a reply.
49. As the author, I want each comment count of the admin and the dashboard to include the replies, so that the counts show all comments.

**Reply emails**

50. As a commenter, I want an unchecked "email me when someone replies" checkbox on the comment form, so that I choose reply emails for this one comment.
51. As a commenter, I want no checkbox on the reply form, so that only the commenter of a top-level comment gets reply emails.
52. As a commenter who checked the box, I want one reply email at the first approval of each reply from someone else, so that I know at once when someone answers.
53. As a commenter, I want no reply email for my own reply, so that I do not get my own text.
54. As a commenter, I want no reply email for a reply that is approved again after a reject, so that I get one email for each reply.
55. As a commenter, I want no reply email for a reply that someone posted before my comment was approved, so that I get no batch of old replies at a late approval.
56. As a commenter, I want the subject "<name> replied to your comment on <post title>", so that I know who answered and where.
57. As a commenter, I want the email to show the post title, an excerpt of my comment, and the full rendered reply, so that I can read the answer in my inbox.
58. As a commenter, I want a `Read the thread` button that opens the reply on the post, so that I can see the reply in its thread.
59. As a commenter, I want the post to scroll to the top-level comment when the reply is gone, so that the link still goes to my thread.
60. As a commenter, I want the line "Reply on the post" under the button, so that I do not answer by email.
61. As a commenter, I want the footer to tell me why the email came, so that I remember that I asked for it.
62. As a commenter, I want a stop link in each reply email, so that I can stop the reply emails of that comment.
63. As a commenter, I want the stop page to ask me to confirm with `[stop reply emails]`, so that a mail scanner that opens the link does not stop my emails.
64. As a commenter, I want the stop page to say "You get no more reply emails for this comment." after the stop, and again at each later open, so that I know the stop worked.
65. As a commenter, I want the stop to apply only to that one comment, so that my other comments keep their choice.
66. As a commenter, I want the stop page to tell me when a link does not work, with an email address for help, so that I can still stop the emails.
67. As a commenter, I want the privacy policy to name the reply email, so that I know what the blog does with my email address.
68. As a commenter, I want the email field to say that the address is used to verify me and for the reply emails that I ask for, so that the placeholder is true.
69. As the author, I want a banned commenter to get no reply email, and the consent to stay after an unban, so that a ban stops emails with no loss of the choice.
70. As the author, I want the consent and its date kept on the comment, so that I have a record of each consent.
71. As the author, I want each reply email sent once also when the queue tries again, so that a failure does not send two emails.
72. As the author, I want no timer, no delayed message, and no digest, so that the backend on Flex Consumption runs only at an approval.
73. As the author, I want each comment from before this feature to send no reply email, so that no commenter gets an email they did not ask for.

## Implementation Decisions

**Domain model (Core).**

- **Parent.** A comment gets an optional parent: the ID of its top-level comment. A comment with no parent is a top-level comment. A reply cannot be the parent of a reply.
- **Writer.** A comment gets a writer: `Commenter of Commenter` or `Author`. The author has no commenter record. Each existing comment reads as a top-level comment from its commenter.
- **Reply-email consent.** A top-level comment gets an optional reply-email consent. It has the consent date, a random stop token (a GUID), and an optional stop date. A comment with no consent sends no reply email. This includes each comment from before this feature.
- **Send record.** A reply gets an optional reply-email send date. The send writes it after ACS accepts the email.
- **Table.** The comment entity of the table gets the optional properties `ParentId`, `Writer`, `ReplyEmailsConsentDate`, `ReplyEmailsStopToken`, `ReplyEmailsStopDate`, and `ReplyEmailSentDate`. `CommenterId` is empty when the writer is the author. A missing `Writer` reads as a commenter, so the table gets no migration. The stop page finds a comment by its token, so the backend needs a way to look up a token. Use a small `replyEmailStops` table from token to post slug and comment ID, written with the consent.

**Pure rules (Core).** Put each rule in a pure function with tests:

- **Reply check.** A commenter can reply only to an approved top-level comment of the same post, after the publish date of the post. Each other case is a refusal with a named reason: the parent is a reply, new, rejected, missing, or on an upcoming post.
- **Approval.** The status of the commenter alone sets the approval of a reply from a commenter, as for a comment. A new commenter gets new, a trusted commenter gets approved, and a banned commenter gets a reply that the blog does not keep. A reply from the author is approved at the post.
- **Author reply.** The author can reply to a new or approved comment, and cannot reply to a rejected comment. On a reply row, the reply goes to the top-level comment of that thread. When the comment that the author replies to is new, the reply approves it, and also its top-level comment if that comment is new.
- **Visibility.** Readers see a reply only when the reply and its top-level comment are approved. A reject of a top-level comment writes no change to the replies.
- **Delete.** A delete of a top-level comment removes the thread, also a reply from the author. A delete of a commenter removes each comment of the commenter and each thread that a comment of the commenter starts. A reply of that commenter in another thread goes alone. Each delete gives the number of other comments that go with it.
- **Reply-email gate.** A reply sends a reply email when all of these are true:
  - It is approved for the first time.
  - Its top-level comment is approved at that time.
  - The top-level comment has a consent with no stop.
  - The writer of the reply is not the commenter of the top-level comment.
  - The reply has no send record.

  The send function checks the gate again at the send, and also checks that the recipient is not banned.

**Endpoints.**

- **`POST blog/posts/{slug}/comments` (changed).**
  - The request gets an optional `replyTo` (the ID of a top-level comment) and an optional `replyEmails` flag. A request with both is refused with `400`, because a reply has no checkbox.
  - A refused reply check answers `409` with its reason.
  - A checked `replyEmails` on a top-level comment stores the consent and its date, and makes the stop token.
  - The verify flow is the same as for a comment. The redirect URI of the widget adds the ID of the top-level comment, so that the widget opens the reply form of that thread at the return.
- **`GET blog/posts/{slug}/comments` (changed).**
  - Each comment gets `replyTo` (the ID of its top-level comment, or null) and `writer` (`commenter` or `author`).
  - For a reply from the author, `commenter` is empty. The widget shows the author name from its props.
  - The endpoint gives only the approved replies of an approved top-level comment.
  - It never gives a stop token, a consent, or an email address.
- **`POST internal/posts/{slug}/comments/{commentId}/replies` (new).** The author reply. The request is `{ "content": "<source>" }`. The answer is the new reply and the approval changes of its parent. A rejected parent gets `409`.
- **Admin comment answers (changed).** The queue, the comments of a post, and the commenter page give, for each reply, the ID, name, plain-text excerpt (200 characters), approval, and full HTML of its top-level comment. For each top-level comment, they give a flag that is true when its commenter gets reply emails now: a consent, no stop, and no ban.
- **`DELETE` of a comment and of a commenter (changed).** These follow the delete rules above.
- **`GET blog/reply-emails/{token}` (new).** The stop page state, with the post title and `active`, `stopped`, or `unknown`. A deleted comment gives `unknown`.
- **`POST blog/reply-emails/{token}/stop` (new).** It writes the stop date. A second stop answers as stopped and writes nothing.

**Notification.** A new reply from a commenter sends the new-comment Web Push. The title is "<name> replied to <name of the top-level comment>". The body is the first 120 characters of the plain text of the reply. A reply from the author sends no notification.

**Reply email send.**

- **Queue.** Each first approval of a reply puts a message with the slug and the reply ID on a new Service Bus queue `reply_email`. This includes the approval at the post of a reply from a trusted commenter or from the author, and the approval of a reply in the queue.
- **Send function.** A new queue function `Queues-ProcessReplyEmailMessage` reads the reply, its top-level comment, the consent, the stop, the ban, and the send record at that time. It applies the gate, renders the email, and sends it.
- **Send record.** It writes the send record after ACS accepts the email. It skips a reply that has the record, so a Service Bus retry sends no second email.
- **Failures.** A message that fails each retry goes to the dead-letter queue, with no other alert. The approval answers the admin or the commenter at once.
- **Setup.** A setup script creates the queue, as `scripts/setup-new-comment-queue.sh` does for `new_comment`. Run it before the deploy.

**Reply email content.** A pure compose function makes the subject, the HTML, and the plain-text part from the reply, its top-level comment, the post, and the stop URL.

- **Sender.** The `EmailSender` setting, with no Reply-To. The email has no `List-Unsubscribe` or `List-Unsubscribe-Post` header.
- **Subject.** "<name> replied to your comment on <post title>". For a reply from the author, <name> is the post author name.
- **Template.** The card of the verification email. From top to bottom:
  1. The logo.
  2. "Hey <commenter name>!"
  3. "<name> replied to your comment on <post title>:" with the title as a link, and "(author)" after the post author name.
  4. A block quote with the first 200 characters of the plain text of the top-level comment.
  5. The HTML of the reply from the comment renderer, with inline styles for code, block quotes, and lists. The email adds no `tk-*` colors.
  6. The `Read the thread` button and the line "Reply on the post".
  7. A rule and the footer: "You asked for reply emails when you commented on <post title>.", the stop link, and the copyright line.

  The email has no sign-off, no newsletter invitation, and no other post.
- **Plain-text part.** The same parts, with the reply as plain text from the comment renderer.
- **Thread link.** The post URL with the query `?thread=<top-level comment id>` and the fragment `#comment-<reply id>`. A removed reply is not in the comments answer, so the query gives the widget the ID of its top-level comment for the fallback.
- **Stop link.** `/comments/stop/?token=<token>` on the site of the stage.

**Comments widget.**

- **Props.** The generator puts the `author` name of the post front matter in the props of the Comments widget.
- **Threads.**
  - Each comment element gets the ID `comment-<id>`.
  - A thread shows the top-level comment, then its replies with an indent of 1ch and a 1px left line in the line color.
  - A reply from the author shows the author name, an `[author]` badge, and a 2px left line in `--accent-2`.
  - A thread with more than 3 replies shows the 2 newest and `[show N earlier replies]`. The widget keeps this state in memory only.
- **Reply form.**
  - Each thread has one `[reply]` at its end. `[reply]` opens the reply form in place of the control.
  - The form has a "reply to <name>" line, the tabs, the preview, the help line of the comment form, `[post reply]`, and `[cancel]`.
  - For a verified browser, one line `as <name> (<email>) [change]` replaces the fields, and `[change]` opens them. The reply form and the page form share the name and the email.
  - One reply form is open at a time. The widget keeps the text of each closed form in memory, and the text comes back at the next open. A reload loses the text.
- **Verify return.** When the return URL has the ID of a top-level comment, the widget opens the reply form of that thread with the kept text, scrolls to it, and shows "Your email address is verified. Select [post reply] to post your reply."
- **After a post.** An approved reply shows at the end of its thread. A new reply shows nothing, and the thread shows "Your reply is posted. It shows here after the author approves it."
- **Fragment.** After the comments load, the widget reads a `#comment-<id>` fragment. It opens the earlier replies of the thread if they hide the comment, scrolls to the comment, and marks it for a short time. When the comment is not in the list, the widget scrolls to the comment that the `thread` query names. When that comment is not in the list either, or the URL has no `thread` query, the widget scrolls to the comments section. It shows no message.
- **Checkbox.** The page form gets an unchecked "email me when someone replies" checkbox. The reply form has none.
- **Email field.** The placeholder "only to verify you" changes to "to verify you, and for reply emails if you ask".

**Stop page (new).** The generator makes a static page at `/comments/stop/` with a new `stop-reply-emails` widget, as the unsubscribe page does.

- At each open, the widget reads the state with one GET.
- For an active token, it shows "Stop reply emails for your comment on <post title>?" and `[stop reply emails]`. Only the button sends the POST.
- For a stopped token, it shows "You get no more reply emails for this comment."
- For an unknown or missing token, it shows that the link does not work.
- The page shows the mailto help line, as the unsubscribe page does.

**Admin.**

- **Reply form.** `[reply]` on each row of the queue and of the comments of a post opens an inline reply form under the row. On a rejected row, the action does not show. The form has `[write]` and `[preview]` tabs, the shared preview endpoint, the length limit, and the link URLs of the admin stylesheet.
- **Messages.** On a new row, the action tells the author that the reply approves the comment. When the flag of the top-level comment is true, the form shows "<name> gets an email with this reply."
- **Rows.** A reply row in the queue and on the commenter page shows "reply to <name>" and the plain-text excerpt. The excerpt opens the full HTML of the top-level comment. When the top-level comment is rejected, the row says that readers will not see the reply.
- **Comments of a post.** The page shows each thread: the top-level comment, then its replies indented, oldest first, each with its status.
- **Delete.** The confirm dialog of a delete of a comment or of a commenter gives the number of other comments that go.
- **Counts.** Each comment count includes the replies, and the dashboard count too.

**Privacy policy.** The Comments section names the reply email, the opt-in checkbox, the record of the consent and its date, and the stop link, before the first send.

**Glossary.** The terms **Top-level comment**, **Reply**, **Thread**, and **Reply email** are already in `GLOSSARY.md`. This spec adds no term.

**Prototype.** The branch `prototype/comment-threads` has the thread layout, variant A. The branch is not for merge.

## Testing Decisions

A good test checks the external behavior of a seam: the answer of an action for a request and the state of its fakes, the composed email for a reply, the next model and effect of a widget or an admin page for a message, or the HTML of a generated page. It does not check table entities, private helpers, or the Service Bus SDK.

The spec uses four existing seams.

1. **Core** (Core.Tests with fake dependencies, as `ModerationTests`, `NewCommentTests`, and `StartNewPostDeliveryTests`). Test these:
   - Each reason of the reply check.
   - The approval of a reply for a new, trusted, and banned commenter, and for the author.
   - An author reply to a new reply that approves the reply and its new top-level comment.
   - The refusal of an author reply to a rejected comment.
   - Visibility after a reject and a new approval of a top-level comment.
   - A thread delete and a commenter delete, with their counts.
   - Each condition of the reply-email gate: a first approval, a parent that is not approved, no consent, a stop, a reply from the commenter of the top-level comment, a send record, and a ban at the send.
   - One queued message for each first approval.
   - The send record after a send, and no second send at a retry.
   - The stop states and a second stop.
   - The notification title of a reply.
   - The compose function: the subject for a commenter and for the author, the excerpt, the thread URL, the stop URL, the footer, the plain-text part, and the absence of commercial content.
2. **Widgets** (Widgets.Tests through `update`, as `CommentsTests` and `UnsubscribeTests`). Test these:
   - The thread order and the collapse at more than 3 replies.
   - One open reply form, and the kept text.
   - The verified one-line identity and `[change]`.
   - The verify return to a thread.
   - The answers after a post for an approved and a new reply.
   - The fragment target and its fallbacks.
   - The checkbox only on the page form.
   - The stop widget: active, stopped, unknown, the confirm, and a failed POST.
3. **Admin** (Admin.Tests through `update`, as `QueueTests` and `PostCommentsTests`). Test these:
   - The reply action on new, approved, rejected, and reply rows.
   - The approval note on a new row.
   - The reply email line.
   - The reply line, the excerpt, and the rejected-parent note.
   - The thread view of the comments of a post.
   - The delete counts.
4. **Generator** (Generator.Tests, as `UnsubscribePageTests` and `WidgetsTests`). Test these:
   - The author name in the Comments props.
   - The stop page with its widget mount and help line.
   - The new placeholder text.
   - The reply email in the privacy text.

The `site` CI job and the `npm test` happy-dom run of each widget entry catch a broken mount.

## Out of Scope

- Nested replies, and a reply to a reply.
- A top-level comment from the author.
- An edit of a reply or of a comment.
- A reply email to each commenter in a thread, a digest, a delayed email, and a later opt-in.
- A "stop all" link, and `List-Unsubscribe` headers with a one-click stop.
- Code colors in the reply email.
- An alert for a dead-lettered reply email.
- A rate limit on replies or on the preview endpoint.
- A group by thread in the moderation queue.

## Further Notes

Decisions on the map:

- [Choose the shape of a reply thread](https://github.com/tw0po1nt/twopoint-website-staging/issues/345)
- [Choose how readers see the reply of the author](https://github.com/tw0po1nt/twopoint-website-staging/issues/346)
- [Choose how a reply goes through moderation](https://github.com/tw0po1nt/twopoint-website-staging/issues/347)
- [Survey the rules for a reply email to a commenter](https://github.com/tw0po1nt/twopoint-website-staging/issues/348)
- [Choose when a commenter gets a reply email](https://github.com/tw0po1nt/twopoint-website-staging/issues/349)
- [Prototype the thread layout of the Comments widget on a phone](https://github.com/tw0po1nt/twopoint-website-staging/issues/351)
- [Choose the content of the reply email](https://github.com/tw0po1nt/twopoint-website-staging/issues/356)

The moderation decision of the admin map says that a commenter gets no email. The reply email changes that decision for a reply only.

The reply email keeps out commercial content, so it stays outside the CAN-SPAM opt-out rules. A future change that adds a newsletter invitation or a list of posts to the email must check those rules again first.

The table names `replyEmailStops` and the queue name `reply_email` are proposals. An implementer can change them, as long as the setup script and `AGENTS.md` agree.

