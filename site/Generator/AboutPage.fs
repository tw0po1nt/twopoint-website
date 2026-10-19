[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.AboutPage

open Oxpecker.ViewEngine

let urlPath = "/about/"

let robots = Layout.Robots.Index

let render (config: StageConfig) (assets: Assets) (widgets: Widgets) (posts: Post list) =
  let page: Layout.Page =
    { Title = Some "About"
      Description = "Matthew Watt is a full-stack software engineer who writes about functional programming."
      UrlPath = Some urlPath
      Robots = robots
      OgType = Layout.OgType.Website
      OgImage = assets.DefaultImage
      Current = None
      Banner = None }

  let photo = assets.AboutPhoto

  main (class' = "page") {
    p (class' = "path") {
      a (href = "/") { "~/twopoint" }
      " $ whoami"
    }

    h1 () { "Matthew Watt" }
    p (class' = "excerpt") { "A full-stack software engineer." }

    div (class' = "about") {
      img (
        class' = "about-photo",
        src = photo.UrlPath,
        alt = "A photo of Matthew Watt",
        width = photo.Size.Width,
        height = photo.Size.Height
      )

      div (class' = "prose") {
        p () { "I'm a full-stack software engineer." }

        p () {
          "My passion for software development started with mobile app development, specifically Swift, Objective-C, \
           and Apple platforms. I've since broadened my skillset to include frontend web technologies such as React \
           and TypeScript, as well as backend technologies like C#, F#, Haskell, Rust, ASP.NET Core, SQL, and others."
        }

        p () { "Learning, applying, and teaching functional programming has become a passion of mine." }

        h2 () { "Get in touch" }

        p () {
          "Send me an email at "
          a (href = "mailto:matt@twopoint.dev") { "matt@twopoint.dev" }
          ", or find me on "
          a (href = Layout.githubUrl, target = "_blank", rel = "noopener noreferrer") { "GitHub" }
          "."
        }
      }
    }
  }
  |> Layout.render config assets widgets posts page
