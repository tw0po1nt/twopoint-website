[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.SubscribePage

open Oxpecker.ViewEngine

let urlPath = "/subscribe/"

let robots = Layout.Robots.Index

let render (config: StageConfig) (assets: Assets) (widgets: Widgets) (posts: Post list) =
  let page: Layout.Page =
    { Title = Some "Subscribe"
      Description = "Get an email for each new post on TwoPoint."
      UrlPath = Some urlPath
      Robots = robots
      OgType = Layout.OgType.Website
      OgImage = assets.DefaultImage
      Current = None
      Banner = None }

  main (class' = "page") {
    p (class' = "path") {
      a (href = "/") { "~/twopoint" }
      " $ subscribe --email"
    }

    h1 () { "Subscribe" }
    p (class' = "excerpt") { "Get an email for each new post. Each email has a link to unsubscribe." }
    Mount.subscribe config
  }
  |> Layout.render config assets widgets posts page
