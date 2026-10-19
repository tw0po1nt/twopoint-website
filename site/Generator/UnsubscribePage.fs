[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.UnsubscribePage

open Oxpecker.ViewEngine

let urlPath = "/unsubscribe/"

let render (config: StageConfig) (assets: Assets) (widgets: Widgets) (posts: Post list) =
  let page: Layout.Page =
    { Title = Some "Unsubscribe"
      Description = "Stop the emails of new posts on TwoPoint."
      UrlPath = Some urlPath
      Robots = Layout.Robots.Index
      OgType = Layout.OgType.Website
      OgImage = assets.DefaultImage
      Current = None
      Banner = None }

  main (class' = "page") {
    p (class' = "path") {
      a (href = "/") { "~/twopoint" }
      " $ unsubscribe"
    }

    h1 () { "Unsubscribe" }
    Mount.unsubscribe config

    p (class' = "unsubscribe-help") {
      "If the link does not work, send an email to "
      a (href = "mailto:matt@twopoint.dev") { "matt@twopoint.dev" }
      "."
    }
  }
  |> Layout.render config assets widgets posts page
