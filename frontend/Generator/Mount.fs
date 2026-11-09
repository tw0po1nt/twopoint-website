[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.Mount

open TwoPoint.Site.Widgets

open Oxpecker.ViewEngine
open System

/// `fallback` is the static HTML that stays when the widget script does not run.
let view (widget: Contract.Widget) (fallback: HtmlElement) =
  let mount =
    Contract.toAttributes widget
    |> List.fold (fun (element: div) (name, value) -> element.attr (name, value)) (div ())

  mount { fallback }

let private apiOrigin (config: StageConfig) = config.ApiUrl.GetLeftPart UriPartial.Authority

let comments (config: StageConfig) (slug: string) (author: string) =
  view
    (Contract.Widget.Comments { Api = apiOrigin config; Slug = slug; Author = author })
    (p (class' = "comments-fallback") { "The comments need JavaScript." })

let unsubscribe (config: StageConfig) =
  view
    (Contract.Widget.Unsubscribe { Api = apiOrigin config })
    (p (class' = "unsubscribe-fallback") { "The unsubscribe link needs JavaScript." })

let stopReplyEmails (config: StageConfig) =
  view
    (Contract.Widget.StopReplyEmails { Api = apiOrigin config })
    (p (class' = "stop-reply-emails-fallback") { "The stop link needs JavaScript." })

let confirmSubscription (config: StageConfig) =
  view
    (Contract.Widget.ConfirmSubscription { Api = apiOrigin config })
    (p (class' = "confirm-subscription-fallback") { "The confirm link needs JavaScript." })

let subscribe (config: StageConfig) =
  view
    (Contract.Widget.Subscribe { Api = apiOrigin config })
    (p (class' = "subscribe-fallback") {
      "The subscribe form needs JavaScript. You can also follow the "
      a (href = "/rss.xml") { "RSS feed" }
      "."
    })
