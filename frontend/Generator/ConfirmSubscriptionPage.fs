[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.ConfirmSubscriptionPage

open TwoPoint.Site.Render

open Oxpecker.ViewEngine

let urlPath = SitePage.urlPath SitePage.ConfirmSubscription

/// Only the link of a confirmation email opens this page.
let robots = Layout.Robots.NoIndex

let render (config: StageConfig) (assets: Assets) (widgets: Widgets) (posts: Post list) =
  let page: Layout.Page =
    { Title = Some "Confirm subscription"
      Description = "Confirm your subscription to get new posts by email."
      UrlPath = Some urlPath
      Robots = robots
      OgType = Layout.OgType.Website
      OgImage = assets.DefaultImage
      Current = None
      Banner = None }

  main (class' = "page") {
    p (class' = "path") {
      a (href = "/") { "~/twopoint" }
      " $ subscribe confirm"
    }

    h1 () { "Confirm subscription" }
    Mount.confirmSubscription config

    p (class' = "confirm-subscription-help") {
      "If the link doesn't work, email "
      a (href = "mailto:matt@twopoint.dev") { "matt@twopoint.dev" }
      "."
    }
  }
  |> Layout.render config assets widgets posts page
