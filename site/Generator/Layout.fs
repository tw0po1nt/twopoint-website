[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.Layout

open Oxpecker.ViewEngine
open System
open System.Globalization

[<RequireQualifiedAccess>]
type Robots =
  | Index
  | NoIndex

[<RequireQualifiedAccess>]
type OgType =
  | Website
  | Article

type Page =
  { /// `None` gives the site title alone.
    Title: string option
    Description: string
    /// `None` gives no canonical URL and no `og:url`.
    UrlPath: string option
    Robots: Robots
    OgType: OgType
    OgImage: OgImage
    /// The post that the explorer marks.
    Current: Post option
    /// The strip above the header, for example the upcoming post banner.
    Banner: HtmlElement option }

let private emDash = string (char 0x2014)

let private googleSiteVerification = "Ej-5oQD6F_zuV5I59TTGQwLOdR6h4mpj6Fe-uZlX38k"

let private feedTitle = $"TwoPoint{char 0x2019}s Blog"

let private githubUrl = "https://github.com/tw0po1nt"

let private links =
  [ "posts", "/posts/"
    "one-offs", "/category/one-offs/"
    "series", "/series/"
    "talks", "/category/talks/"
    "subscribe", "/subscribe/"
    "about", "/about/" ]

let isoTime (time: DateTimeOffset) =
  time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)

/// A time for the head script, in the form of `Date.toISOString`, for example `2026-10-29T13:00:00.000Z`, so that the
/// script can compare it with the current time as text.
let scriptTime (time: DateTimeOffset) =
  time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)

let shownDate (time: DateTimeOffset) =
  time.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)

let url (config: StageConfig) (urlPath: string) = Uri(config.SiteUrl, urlPath).AbsoluteUri

let private robots (config: StageConfig) (page: Page) =
  match config.Indexable, page.Robots with
  | false, _ -> "noindex,nofollow"
  | true, Robots.Index -> "index,follow"
  | true, Robots.NoIndex -> "noindex,follow"

let private head (config: StageConfig) (assets: Assets) (widgets: Widgets) (scripts: WidgetScripts) (page: Page) =
  let title =
    match page.Title with
    | Some title -> $"{title} {emDash} {config.SiteTitle}"
    | None -> config.SiteTitle

  let ogType =
    match page.OgType with
    | OgType.Website -> "website"
    | OgType.Article -> "article"

  head () {
    meta (charset = "utf-8")
    meta (name = "viewport", content = "width=device-width, initial-scale=1")
    Tags.title () { title }
    meta (name = "description", content = page.Description)
    meta (name = "robots", content = robots config page)

    match page.UrlPath with
    | Some urlPath -> link (rel = "canonical", href = url config urlPath)
    | None -> ()

    meta().attr("property", "og:title").attr ("content", page.Title |> Option.defaultValue config.SiteTitle)
    meta().attr("property", "og:description").attr ("content", page.Description)

    match page.UrlPath with
    | Some urlPath -> meta().attr("property", "og:url").attr ("content", url config urlPath)
    | None -> ()

    meta().attr("property", "og:image").attr ("content", url config page.OgImage.UrlPath)
    meta().attr("property", "og:image:width").attr ("content", string page.OgImage.Size.Width)
    meta().attr("property", "og:image:height").attr ("content", string page.OgImage.Size.Height)
    meta().attr("property", "og:type").attr ("content", ogType)
    meta().attr("property", "og:locale").attr ("content", "en")
    meta().attr("property", "og:site_name").attr ("content", config.OgSiteName)
    meta (name = "twitter:card", content = "summary_large_image")
    meta (name = "google-site-verification", content = googleSiteVerification)
    link (rel = "alternate", type' = "application/rss+xml", title = feedTitle, href = "/rss.xml")
    link(rel = "icon", href = assets.Favicons.Ico).attr ("sizes", "32x32")
    link (rel = "icon", type' = "image/svg+xml", href = assets.Favicons.Svg)
    link (rel = "apple-touch-icon", href = assets.Favicons.AppleTouch)
    link (rel = "stylesheet", href = assets.Stylesheet)
    script () { raw widgets.HeadScript }

    for href in scripts.Preloads do
      link (rel = "modulepreload", href = href)

    for src in scripts.Entries do
      script (type' = "module", src = src)

    match config.PulseSiteId with
    | Some siteId -> script(src = "https://pulse.w7ech.com/cdn/s.js", defer = true).data ("site", siteId)
    | None -> ()
  }

let private logo () =
  Fragment() {
    "two"
    span () { "</>" }
    "point"
  }

let private header () =
  header (class' = "site-header") {
    a (class' = "logo", href = "/") { logo () }

    nav(class' = "site-nav").attr ("aria-label", "Site") {
      for text, href in links do
        a (href = href) { text }
    }

    button(class' = "theme-toggle", type' = "button").attr("aria-label", "Switch the theme").data ("theme-toggle", "")
    button (class' = "explorer-toggle", type' = "button", popovertarget = "explorer") { "[explorer]" }
  }

let private footerLinks () =
  Fragment() {
    a (href = "/privacy/") { "/privacy" }
    a (href = githubUrl) { "github" }
    a (href = "/rss.xml") { "rss.xml" }
  }

let private explorer (config: StageConfig) (posts: Post list) (current: Post option) =
  let isCurrent (post: Post) = current |> Option.exists (fun current -> current.Slug = post.Slug)

  aside(id = "explorer", class' = "explorer", popover = "auto").attr ("aria-label", "Explorer") {
    button (class' = "explorer-close", type' = "button", popovertarget = "explorer", popovertargetaction = "hide") {
      "[close]"
    }

    nav(class' = "explorer-nav").attr ("aria-label", "Site") {
      for text, href in links do
        a (href = href) { text }
    }

    p (class' = "label") { "explorer" }

    ul (class' = "tree") {
      for year, posts in Blog.byYear posts do
        li (class' = "dir") { $"posts/{year}/" }

        for post in posts do
          li (class' = if isCurrent post then "is-current" else null) {
            time (datetime = isoTime post.FrontMatter.PublishDate) {
              post.FrontMatter.PublishDate.UtcDateTime.ToString("MM-dd", CultureInfo.InvariantCulture)
            }

            a(href = Post.urlPath post).attr ("aria-current", if isCurrent post then "page" else null) {
              post.FrontMatter.Title
            }

            if post.FrontMatter.Category.Slug = "talks" then
              span (class' = "kind") { "talk" }
          }

      match Blog.series posts with
      | [] -> ()
      | series ->
        li (class' = "dir") { "series/" }

        for series, count in series do
          li (class' = "tree-series") {
            a (href = $"/series/{series.Slug}/") { $"{series.Slug}/" }
            span (class' = "count") { string count }
          }
    }

    match Blog.tags posts with
    | [] -> ()
    | tags ->
      p (class' = "label") { "tags" }

      p (class' = "tags") {
        for tag, _ in tags do
          a (href = $"/tag/{tag.Slug}/") { $"#{tag.Title}" }
      }

    p (class' = "label") { "subscribe" }
    Mount.subscribe config
    p (class' = "explorer-foot") { footerLinks () }
  }

let private footer () =
  footer (class' = "site-footer") {
    span (class' = "logo") { logo () }
    footerLinks ()
  }

/// `posts` are the posts that the explorer lists.
let render (config: StageConfig) (assets: Assets) (widgets: Widgets) (posts: Post list) (page: Page) (main: HtmlElement) =
  let body =
    body () {
      match page.Banner with
      | Some banner -> banner
      | None -> ()

      header ()
      explorer config posts page.Current
      main
      footer ()
    }
    |> Render.toString

  html (lang = "en") {
    head config assets widgets (Widgets.scripts widgets body) page
    raw body
  }
  |> Render.toHtmlDocBytes
