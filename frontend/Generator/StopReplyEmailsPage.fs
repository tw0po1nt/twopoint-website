[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.StopReplyEmailsPage

open TwoPoint.Site.Render

open Oxpecker.ViewEngine

let urlPath = SitePage.urlPath SitePage.StopReplyEmails

/// Only the stop link of a reply email opens this page.
let robots = Layout.Robots.NoIndex

let render (config: StageConfig) (assets: Assets) (widgets: Widgets) (posts: Post list) =
  let page: Layout.Page =
    { Title = Some "Stop reply emails"
      Description = "Stop getting reply emails for your comment."
      UrlPath = Some urlPath
      Robots = robots
      OgType = Layout.OgType.Website
      OgImage = assets.DefaultImage
      Current = None
      Banner = None }

  main (class' = "page") {
    p (class' = "path") {
      a (href = "/") { "~/twopoint" }
      " $ comments stop"
    }

    h1 () { "Stop reply emails" }
    Mount.stopReplyEmails config

    p (class' = "stop-reply-emails-help") {
      "If the link doesn't work, email "
      a (href = "mailto:matt@twopoint.dev") { "matt@twopoint.dev" }
      "."
    }
  }
  |> Layout.render config assets widgets posts page
