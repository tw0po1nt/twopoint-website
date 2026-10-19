[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.HomePage

open TwoPoint.Site.Widgets

open Oxpecker.ViewEngine
open System

let urlPath = "/"

/// The banner for the first post after `buildTime`. The head script hides it after the publish date.
let private banner (buildTime: DateTimeOffset) (posts: Post list) =
  Blog.upcoming buildTime posts
  |> Option.map (fun post ->
    let publishDate = post.FrontMatter.PublishDate

    p(class' = "upcoming").attr (HideAt.attribute, Layout.scriptTime publishDate) {
      span (class' = "badge") { "upcoming" }
      a (href = Post.urlPath post) { post.FrontMatter.Title }
      time (datetime = Layout.isoTime publishDate) { Layout.shownDate publishDate }
    }
    :> HtmlElement)

let private intro () =
  section (class' = "intro") {
    PostLog.prompt "cat README"
    h1 () { "A blog about programming, mostly functional." }
    p (class' = "byline") { "By Matthew Watt. F#, C#, Swift, and the odd conference talk." }
  }

let private latestCard (posts: Post list) (post: Post) =
  let frontMatter = post.FrontMatter

  section (class' = "latest") {
    PostLog.prompt "head -n 1 posts"

    article () {
      p (class' = "meta") {
        time (datetime = Layout.isoTime frontMatter.PublishDate) { Layout.shownDate frontMatter.PublishDate }
        " · "

        match Blog.seriesPosition posts post with
        | Some position -> a (href = $"/series/{position.Series.Slug}/") { $"series/{position.Series.Slug}" }
        | None -> a (href = $"/category/{frontMatter.Category.Slug}/") { $"category/{frontMatter.Category.Slug}" }

        if post.Body.ReadingTime > 0 then
          $" · {post.Body.ReadingTime} min"
      }

      h2 () { a (href = Post.urlPath post) { frontMatter.Title } }
      p () { frontMatter.Excerpt }
      a (class' = "read", href = Post.urlPath post) { "read →" }
    }
  }

let private log (posts: Post list) =
  section (class' = "log") {
    PostLog.prompt "ls -l posts --sort=date"
    PostLog.years posts
  }

let private seriesList (series: (Series * int) list) =
  section (class' = "series-list") {
    PostLog.prompt "ls series/"
    PostLog.series series
  }

let private subscribe (config: StageConfig) =
  section (class' = "home-subscribe") {
    PostLog.prompt "subscribe --email"
    Mount.subscribe config
    p (class' = "note") { "Get an email for each new post. Each email has a link to unsubscribe." }
  }

let render (config: StageConfig) (assets: Assets) (widgets: Widgets) (buildTime: DateTimeOffset) (posts: Post list) =
  let latest = Blog.latest posts

  let page: Layout.Page =
    { Title = None
      Description = config.Description
      UrlPath = Some urlPath
      Robots = Layout.Robots.Index
      OgType = Layout.OgType.Website
      OgImage = assets.DefaultImage
      Current = None
      Banner = banner buildTime posts }

  main (class' = "page home") {
    intro ()

    match latest with
    | Some post -> latestCard posts post
    | None -> ()

    match posts |> List.filter (fun post -> Some post.Slug <> (latest |> Option.map _.Slug)) with
    | [] -> ()
    | logged -> log logged

    match Blog.series posts with
    | [] -> ()
    | series -> seriesList series

    subscribe config
  }
  |> Layout.render config assets widgets posts page
