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
A post that is not ready to publish. The build checks its front matter and leaves it out of the
output.
_Avoid_: Unpublished post

**Series**:
A group of posts in order. Each post in a series has the slug, the title, and the excerpt of the
series.

**Term**:
A category or a tag of a post. Its slug comes from its title.

**Directive**:
A line of a post that embeds a video or the slides of a talk, for example `::youtube{id="…"}`. The
build fails on a bad directive.

**Upcoming post**:
A post with a publish date in the future. Readers can open it before that date, but they cannot comment
on it until that date.
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
