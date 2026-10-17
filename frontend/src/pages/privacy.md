---
layout: ../layouts/MarkdownLayout.astro
title: Privacy policy
---

_Last updated: October 14, 2026_

This page tells you what data twopoint.dev collects, why it collects the data, and how long it keeps the data.

## Who runs the site

twopoint.dev is a technical blog. For a question about this policy or about your data, send an email to
[matt@twopoint.dev](mailto:matt@twopoint.dev).

## Analytics

The site uses [Pulse](https://w7ech.com/products/pulse) to count visits. Pulse records the pages that you visit and
the links that you click. Pulse runs in its cookieless mode:

- Pulse sets no cookies.
- Pulse keeps no raw IP address, no full user agent, and no city.
- Pulse counts a visitor with a hash. The hash uses a salt that changes each day, so Pulse cannot recognize a visitor
  across days.
- Pulse masks email addresses and long runs of digits in page paths, page titles, and click text.
- Pulse does not read form fields.
- Pulse keeps raw visit detail for up to 180 days. After 180 days, Pulse keeps only aggregate summaries.

You can block Pulse with a content blocker in your browser. The site works the same without Pulse.

## Subscriptions

When you subscribe, the site stores your email address. If you give a name, the site stores your name too. The site
uses this data only to send the newsletter.

Azure Communication Services sends the welcome email and each newsletter. Each email has an unsubscribe link. When you
unsubscribe, the site makes your subscription inactive and sends you no more email.

The site keeps an inactive subscription until you ask for deletion. To delete your subscription, send an email to
[matt@twopoint.dev](mailto:matt@twopoint.dev).

## Comments

When you comment, the site stores a commenter record. The record contains your email address, your name if you give one,
the date of your first comment, and a moderation status.

Your name shows publicly with each of your comments. Your email address stays private. The site sends a validation
email to your address before it accepts comments from you.

The site keeps your commenter record and your comments until you ask for deletion. To delete them, send an email to
[matt@twopoint.dev](mailto:matt@twopoint.dev).

## Browser storage

The site uses no cookies. It keeps these values in the `localStorage` of your browser:

- `commenterValidations`: a map from each email address that you comment with to its commenter validation ID. With
  this value, you do not have to validate your email address again on each visit.
- `theme`: your choice of light or dark mode.
- `pulse_ignore`: the opt-out flag of Pulse. When this flag is set, Pulse counts no visits from your browser.

You can clear these values in the settings of your browser.

## Processors

These companies process data for the site:

- [GitHub](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement) hosts the site
  with GitHub Pages. Its server logs include IP addresses.
- [Pulse](https://w7ech.com/privacy-policy) counts visits.
- [Microsoft Azure](https://www.microsoft.com/en-us/privacy/privacystatement) runs the backend. Azure Table Storage
  stores subscriptions and comments, Azure Functions runs the code, and Azure Communication Services sends each email.

The data is stored in the United States.

## Your rights

You can ask to see, correct, or delete your data. Send an email to [matt@twopoint.dev](mailto:matt@twopoint.dev).
