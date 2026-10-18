[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.HomePage

open TwoPoint.Site.Widgets

open Oxpecker.ViewEngine
open System
open System.Globalization

let urlPath = "/"

let private prompt (command: string) =
  p (class' = "prompt") {
    span () { "~/twopoint" }
    $" $ {command}"
  }

/// The head script hides the banner after this time. It is the form of `Date.toISOString`, for example
/// `2026-10-29T13:00:00.000Z`, so that the script can compare it with the current time as text.
let hideAtTime (time: DateTimeOffset) =
  time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)

/// The banner for the first post after `buildTime`. The head script hides it after the publish date.
let private banner (buildTime: DateTimeOffset) (posts: Post list) =
  Blog.upcoming buildTime posts
  |> Option.map (fun post ->
    let publishDate = post.FrontMatter.PublishDate

    p(class' = "upcoming").attr (HideAt.attribute, hideAtTime publishDate) {
      span (class' = "badge") { "upcoming" }
      a (href = Post.urlPath post) { post.FrontMatter.Title }
      time (datetime = Layout.isoTime publishDate) { Layout.shownDate publishDate }
    }
    :> HtmlElement)

let private intro () =
  section (class' = "intro") {
    prompt "cat README"
    h1 () { "A blog about programming, mostly functional." }
    p (class' = "byline") { "By Matthew Watt. F#, C#, Swift, and the odd conference talk." }
  }

let private latestCard (posts: Post list) (post: Post) =
  let frontMatter = post.FrontMatter

  section (class' = "latest") {
    prompt "head -n 1 posts"

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
    prompt "ls -l posts --sort=date"

    for year, posts in Blog.byYear posts do
      h2 () { string year }

      ol () {
        for post in posts do
          li () {
            time (datetime = Layout.isoTime post.FrontMatter.PublishDate) {
              post.FrontMatter.PublishDate.UtcDateTime.ToString("MM-dd", CultureInfo.InvariantCulture)
            }

            span (class' = "kind") { Blog.kind post }
            a (href = Post.urlPath post) { post.FrontMatter.Title }
          }
      }
  }

let private seriesList (series: (Series * int) list) =
  section (class' = "series-list") {
    prompt "ls series/"

    ul () {
      for series, count in series do
        li () {
          a (href = $"/series/{series.Slug}/") { $"{series.Slug}/" }
          span () { $"""{count} {if count = 1 then "post" else "posts"} · {series.Excerpt}""" }
        }
    }
  }

let private subscribe (config: StageConfig) =
  section (class' = "home-subscribe") {
    prompt "subscribe --email"
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
