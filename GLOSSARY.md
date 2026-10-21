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

**Revision**:
The content of a post at one publish. The author can restore a revision as a pending edit.
_Avoid_: Version, snapshot

**Series**:
A group of posts in order. Each post in a series has the slug, the title, and the excerpt of the
series.

**Term**:
A category or a tag of a post. Its slug comes from its title.

**Directive**:
A line of a post that embeds a video or the slides of a talk, for example `::youtube{id="…"}`. The
build fails on a bad directive.

**Upcoming post**:
A published post with a publish date in the future. Readers can open it before that date, but they
cannot comment on it until that date.
_Avoid_: Scheduled post, future post

**Newsletter**:
The email version of a post. The build makes one newsletter for each post.
_Avoid_: Email post

### Build

**Stage**:
A place where the site runs: local, staging, or production. Each stage has its own site URL and its own
values.
_Avoid_: Environment

**Stage marker**:
The file that the build writes next to the site output. It contains the stage name of the build.

### Subscriptions

**Subscription**:
The record of one email address that gets posts by email. A subscription is active or inactive.
_Avoid_: Subscriber, member, signup

**Reactivation**:
A subscribe request for an email address that has an inactive subscription. The subscription becomes
active again.
_Avoid_: Resubscription

**Delivery**:
One email that the blog sends to a subscription. Each delivery has a type and refers to one post.
_Avoid_: Subscription notification, send

**Welcome**:
A delivery that the blog sends after a new subscription or a reactivation. It contains the newsletter of
the latest post.
_Avoid_: Welcome notification, greeting

**New-post delivery**:
A delivery that contains the newsletter of one post. After the publish date of a post, the author starts
a new-post delivery of that post to each active subscription. A subscription that already has a delivery
of the post does not get a new-post delivery of it.
_Avoid_: Announcement, broadcast, blast

### Comments

**Commenter**:
A person who verified an email address to comment on posts. A commenter is new, trusted, or
banned.
_Avoid_: User, reader

**Comment**:
The text that a commenter posts on one post. A comment is new, approved, or rejected. Readers see
only approved comments.
_Avoid_: Reply, response

**Trusted commenter**:
A commenter whose comments are approved at the time of the post. Only the author makes a commenter
trusted.
_Avoid_: Approved commenter, whitelisted commenter

**Banned commenter**:
A commenter whose new comments the blog does not keep. A banned commenter sees the same messages as
a new commenter.
_Avoid_: Blocked commenter, blacklisted commenter

**Rejected comment**:
A comment that readers do not see. The blog keeps it, and the author can approve it later.
_Avoid_: Hidden comment, deleted comment

**Moderation queue**:
The new comments on all posts, oldest first. The author approves or rejects each one.
_Avoid_: Inbox, pending comments

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
_Avoid_: Alert. Do not use this term for an email to a subscription.

**Registered device**:
A browser or a Home Screen web app where the author turned on notifications.
_Avoid_: Push subscription, subscriber, device token
