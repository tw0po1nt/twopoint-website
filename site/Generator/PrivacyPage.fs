[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.PrivacyPage

open Oxpecker.ViewEngine

let urlPath = "/privacy/"

let robots = Layout.Robots.Index

let render (config: StageConfig) (assets: Assets) (widgets: Widgets) (posts: Post list) (privacy: MarkdownPage) =
  let page: Layout.Page =
    { Title = Some privacy.Title
      Description = privacy.Description
      UrlPath = Some urlPath
      Robots = robots
      OgType = Layout.OgType.Website
      OgImage = assets.DefaultImage
      Current = None
      Banner = None }

  main (class' = "page") {
    p (class' = "path") {
      a (href = "/") { "~/twopoint" }
      " $ cat privacy.md"
    }

    h1 () { privacy.Title }
    div (class' = "prose") { raw privacy.Body.Html }
  }
  |> Layout.render config assets widgets posts page
