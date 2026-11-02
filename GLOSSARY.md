# TwoPoint

twopoint.dev is a technical blog. Readers can comment on posts and subscribe to get posts by email.

## Language

### Posts

**Post**:
One article on the blog. Its slug identifies it.
_Avoid_: Article, entry

**Latest post**:
The post with the most recent publish date that does not set `showOnHomepage: false`. The home page shows it on the latest card.

**Draft**:
A post that the author has not published. The site does not show it. A draft has no publish date.
_Avoid_: Unpublished post

**Published post**:
A post that the author published. The site shows it from the next deploy. Its slug does not change.
_Avoid_: Live post

**Pending edit**:
A saved change to a published post that the site does not show yet. The author publishes it or
discards it.
_Avoid_: Working copy, unpublished changes

**Linking post**:
A published post with a link to a post that the author deletes. The link breaks when the post is
deleted.

**Delete check**:
The check before the delete of a published post. Its outcome is a plain delete, a publish and
delete, or blocked. A publish and delete publishes the pending edit of each linking post first,
because those edits remove each link to the post.

**Revision**:
The content of a post at one publish. The author can restore a revision as a pending edit.
_Avoid_: Version, snapshot

**Series**:
A group of posts in order. A series has a slug, a title, and an excerpt. The author makes a series,
then puts posts in it.

**Term**:
A category or a tag of a post. Its slug comes from its title. A post has one category and can have
tags.

**Directive**:
A line of a post that embeds a video or the slides of a talk, for example `::youtube{id="…"}`. The
build fails on a bad directive.

**Asset**:
An image or a PDF that the author uploads for a post. A post refers to an asset by its URL.
_Avoid_: Upload, attachment, media

**Problem**:
An error or a warning that the checks find in a post. The checks also find problems in a series
record and in a Markdown page, such as the privacy page. An error makes the build fail and blocks a
publish. A warning does neither.
_Avoid_: Issue, lint, diagnostic

**Place**:
The part of a post that a problem is about: a front matter field, a body line, or the whole post.
Body line 1 is the first line after the front matter. A problem on the whole post can keep a file
line, for example a YAML syntax error. The build prints each place as `file:line`.
_Avoid_: Location, position

**Upcoming post**:
A published post with a publish date in the future. Readers can open it before that date, but they
cannot comment on it until that date.
_Avoid_: Scheduled post, future post

**Newsletter**:
The email version of a post. The build makes one newsletter for each post.
_Avoid_: Email post

**Migrated mark**:
The mark on the stored published version of each post from before the admin. The author cannot set or
clear it, and each later publish keeps it. A post with the mark is never a delivery to start.
_Avoid_: Legacy flag, imported post

### Build

**Stage**:
A place where the site runs: local, staging, or production. Each stage has its own site URL and its own
values.
_Avoid_: Environment

**Stage marker**:
The file that the build writes next to the site output. It contains the stage name of the build.

**Deploy**:
One run that builds the site and puts it on one stage. A change to a post is deployed when a
successful production deploy includes it.
_Avoid_: Release, go live

### Subscriptions

**Subscription**:
The record of one email address that gets posts by email. A subscription is active or inactive. An
inactive subscription has the canceled date of its last cancellation, and a reactivation clears that
date.
_Avoid_: Subscriber, member, signup

**Reactivation**:
A subscribe request for an email address that has an inactive subscription. The subscription becomes
active again.
_Avoid_: Resubscription

**Cancellation**:
A request that makes an active subscription inactive. The reader sends it from the unsubscribe link, or
the author sends it from the admin.
_Avoid_: Deactivation, unsubscription

**Delivery**:
One email that the blog sends to a subscription. Each delivery has a type and refers to one post.
_Avoid_: Subscription notification, send

**Welcome**:
A delivery that the blog sends after a new subscription or a reactivation. It contains the newsletter of
the newest post with a publish date in the past and a deployed latest change. Between a publish and its
deploy, a new subscription gets the previous post.
_Avoid_: Welcome notification, greeting

**New-post delivery**:
A delivery that contains the newsletter of one post. The author starts a new-post delivery of a post to
each active subscription. The start is possible only after the publish date of the post and after the
deploy of the latest change to the post. A subscription that already has a delivery of the post does not
get a new-post delivery of it.
_Avoid_: Announcement, broadcast, blast

**Delivery start**:
The record that the author started the new-post delivery of a post, with its date. A post has at most one
delivery start, so the blog refuses a second start of the same post.
_Avoid_: Send record, sent flag

**Delivery to start**:
A published post with a publish date in the past, a deployed latest change, no delivery start, and no
migrated mark.
_Avoid_: Unsent post, pending delivery

### Comments

**Commenter**:
A person who verified an email address to comment on posts. A commenter is new, trusted, or
banned. A delete removes the commenter, each comment of the commenter, and each thread that a
comment of the commenter starts. The email address can then verify again, and it becomes a new
commenter.
_Avoid_: User, reader

**Comment**:
The text that a commenter or the author posts on one post. The author posts only replies. A comment
is new, approved, or rejected. Readers see only approved comments.
_Avoid_: Response

**Top-level comment**:
A comment that is not a reply.
_Avoid_: Parent comment, root comment

**Reply**:
A comment that answers one top-level comment of the same post. A reply cannot answer a reply. A
reply from the author is approved when the author posts it. When the author replies to a new
comment, that comment becomes approved. A commenter can reply only to an approved top-level
comment. Readers see a reply only when the reply and its top-level comment are approved.
_Avoid_: Response, child comment, nested comment

**Thread**:
A top-level comment and its replies. A delete of the top-level comment removes the thread.
_Avoid_: Conversation, discussion

**Trusted commenter**:
A commenter whose comments are approved at the time of the post. Only the author makes a commenter
trusted.
_Avoid_: Approved commenter, whitelisted commenter

**Banned commenter**:
A commenter whose new comments the blog does not keep. A banned commenter sees the same messages as
a new commenter. A ban rejects each comment that the commenter has in the moderation queue. The
approved comments of the commenter stay approved. After an unban, the commenter is new. The rejected
comments stay rejected.
_Avoid_: Blocked commenter, blacklisted commenter

**Rejected comment**:
A comment that readers do not see. The blog keeps it, and the author can approve it later.
_Avoid_: Hidden comment, deleted comment

**Moderation queue**:
The new comments on all posts, oldest first. The author approves or rejects each one.
_Avoid_: Inbox, pending comments

**Reply email**:
An email that tells the commenter of a top-level comment about a new reply in the thread. The
commenter asks for reply emails when they post the top-level comment, and only for that comment. A
reply sends one reply email at its first approval, if the top-level comment is approved at that
time. A reply from the commenter of the top-level comment sends none. A banned commenter gets no
reply email. Each reply email lets the commenter stop the reply emails of that comment. After a
stop, the reply emails of that comment do not start again.
_Avoid_: Reply notification, delivery, comment subscription

### Admin

**Author**:
The one person who writes the blog and uses the admin.
_Avoid_: Owner, editor, admin

**Admin**:
The web app where the author moderates comments, writes and publishes posts, and starts deliveries.
One admin serves all stages.
_Avoid_: CMS, back office, admin app

**Notification**:
A push message to a registered device.
_Avoid_: Alert. Do not use this term for an email to a subscription or for a reply email.

**Registered device**:
A browser or a Home Screen web app where the author turned on notifications.
_Avoid_: Push subscription, subscriber, device token
