[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.NotFoundPage

open Oxpecker.ViewEngine

let sitePath = "404.html"

/// GitHub Pages serves this page at each path with no file, so each link and asset path of the page starts at the root.
let render (config: StageConfig) (assets: Assets) (widgets: Widgets) (posts: Post list) =
  let page: Layout.Page =
    { Title = Some "Page not found"
      Description = "The page does not exist on TwoPoint."
      UrlPath = None
      Robots = Layout.Robots.NoIndex
      OgType = Layout.OgType.Website
      OgImage = assets.DefaultImage
      Current = None
      Banner = None }

  main (class' = "page") {
    p (class' = "path") {
      a (href = "/") { "~/twopoint" }
      " $ cat index.html"
    }

    h1 () { "404: page not found" }
    p (class' = "excerpt") { "No such file or directory. The link can be old, or it can contain an error." }

    p (class' = "page-links") {
      a (href = "/") { "cd ~/twopoint" }
      a (href = "/posts/") { "ls posts/" }
      a (href = "/series/") { "ls series/" }
    }
  }
  |> Layout.render config assets widgets posts page
