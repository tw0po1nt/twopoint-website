namespace TwoPoint.Site.Generator

open AngleSharp.Dom
open Oxpecker.ViewEngine
open PreMailer.Net
open System
open System.Text
open System.Text.RegularExpressions

/// The two CSS files in `site/content/email/`.
type EmailStyles =
  { /// The build moves each rule into the `style` attribute of each element that it selects.
    Inline: string
    /// The one `style` block of the newsletter, as written.
    Embed: string }

[<RequireQualifiedAccess>]
module Newsletter =

  /// The backend fills these slots for each delivery, and refuses a newsletter that does not contain them.
  let introSlot = "%%TWOPOINT_INTRO%%"

  let unsubscribeUrlSlot = "%%TWOPOINT_UNSUBSCRIBE_URL%%"

  let private fonts =
    "https://fonts.googleapis.com/css2?family=Inter:wght@400;600;700&family=JetBrains+Mono:wght@400;600&display=swap"

  let private middot = " · "

  let private labelText (post: Post) =
    match post.FrontMatter.Series with
    | Some series -> $"// latest post{middot}{series.Title.ToLowerInvariant()}"
    | None -> "// latest post"

  let private metaText (post: Post) =
    [ Layout.shownDate post.FrontMatter.PublishDate
      if post.Body.ReadingTime > 0 then
        $"{post.Body.ReadingTime} min"
      match post.FrontMatter.Tags with
      | [] -> ()
      | tags -> tags |> List.map (fun tag -> $"#{tag.Slug}") |> String.concat " " ]
    |> String.concat middot

  let private presentation (element: #HtmlTag) =
    element.attr("role", "presentation").attr("border", "0").attr("cellpadding", "0").attr ("cellspacing", "0")

  let private view (config: StageConfig) (styles: EmailStyles) (post: Post) =
    let host = config.SiteUrl.Host

    html (lang = "en") {
      head () {
        meta().attr("http-equiv", "Content-Type").attr ("content", "text/html; charset=UTF-8")
        meta (name = "viewport", content = "width=device-width, initial-scale=1.0")
        meta (name = "color-scheme", content = "light dark")
        meta (name = "supported-color-schemes", content = "light dark")
        title () { post.FrontMatter.Title }
        link (rel = "stylesheet", href = fonts)
        style().attr ("data-embed", "") { raw styles.Embed }
      }

      body (class' = "tp-canvas") {
        div (class' = "tp-preheader") { post.FrontMatter.Excerpt }

        (presentation (table(class' = "tp-canvas").attr ("width", "100%"))) {
          tr () {
            td(class' = "tp-outer").attr ("align", "center") {
              (presentation (table(class' = "tp-container tp-card").attr ("width", "600"))) {
                tr () {
                  td (class' = "tp-term tp-title-bar") {
                    span (class' = "tp-dot-red") { "●" }
                    raw "&nbsp;"
                    span (class' = "tp-dot-yellow") { "●" }
                    raw "&nbsp;"
                    span (class' = "tp-dot-green") { "●" }
                    span (class' = "tp-path") { "~/twopoint" }
                  }
                }

                tr () { td (class' = "tp-term tp-term-end") { raw introSlot } }

                tr () {
                  td (class' = "tp-px tp-header") {
                    p (class' = "tp-label tp-accent") { labelText post }
                    h1 (class' = "tp-h1 tp-ink") { post.FrontMatter.Title }
                    p (class' = "tp-meta tp-muted") { metaText post }
                    p (class' = "tp-excerpt tp-muted") { post.FrontMatter.Excerpt }
                  }
                }

                tr () { td (class' = "tp-px tp-post") { raw post.Body.EmailHtml } }

                tr () {
                  td (class' = "tp-px tp-action") {
                    (presentation (table ())) {
                      tr () {
                        td (class' = "tp-button") {
                          a (href = Stage.url config (Post.urlPath post)) { $"open on {host} ↗" }
                        }
                      }
                    }
                  }
                }

                tr () {
                  td (class' = "tp-px tp-footer tp-muted") {
                    p () {
                      "You subscribed at "
                      a (href = config.SiteUrl.AbsoluteUri) { host }
                      "."
                    }

                    p () {
                      a (href = unsubscribeUrlSlot) { "$ unsubscribe" }
                      raw "&nbsp;&nbsp;·&nbsp;&nbsp;"
                      a (href = Stage.url config "/privacy/") { "$ privacy" }
                    }
                  }
                }
              }
            }
          }
        }
      }
    }
    |> Render.toHtmlDocString

  let private scheme = Regex("^[A-Za-z][A-Za-z0-9+.-]*:")

  /// The inliner ignores the embed `style` block and the font `link`, so that it neither inlines nor downloads them.
  let render (config: StageConfig) (styles: EmailStyles) (post: Post) : byte array =
    let postUrl = Uri(Stage.url config (Post.urlPath post))
    use premailer = new PreMailer(view config styles post)

    for element in premailer.Document.QuerySelectorAll ".tp-post [href], .tp-post [src]" do
      for name in [ "href"; "src" ] do
        match element.GetAttribute name with
        | null -> ()
        | value when scheme.IsMatch value -> ()
        | value -> element.SetAttribute(name, Uri(postUrl, value).AbsoluteUri)

    // Outlook on Windows shows an image at its width attribute and ignores max-width.
    for image in premailer.Document.QuerySelectorAll ".tp-post img" do
      image.RemoveAttribute "width" |> ignore
      image.RemoveAttribute "height" |> ignore

    let result = premailer.MoveCssInline(ignoreElements = "style, link", css = styles.Inline)
    Encoding.UTF8.GetBytes result.Html
