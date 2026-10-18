[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.PostPage

open Oxpecker.ViewEngine
open System
open System.Globalization

let private isoTime (time: DateTimeOffset) =
  time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)

let private shownDate (time: DateTimeOffset) =
  time.UtcDateTime.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)

let render (config: StageConfig) (post: Post) =
  let frontMatter = post.FrontMatter

  html (lang = "en") {
    head () {
      meta (charset = "utf-8")
      meta (name = "viewport", content = "width=device-width, initial-scale=1")
      title () { $"{frontMatter.Title} \u2014 {config.SiteTitle}" }
    }

    body () {
      main () {
        article () {
          h1 () { frontMatter.Title }
          p () { frontMatter.Excerpt }
          time (datetime = isoTime frontMatter.PublishDate) { shownDate frontMatter.PublishDate }
          raw post.Body.Html
        }
      }
    }
  }
  |> Render.toHtmlDocBytes
