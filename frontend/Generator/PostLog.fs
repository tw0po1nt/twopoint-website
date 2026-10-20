[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.PostLog

open Oxpecker.ViewEngine
open System.Globalization

let prompt (command: string) =
  p (class' = "prompt") {
    span () { "~/twopoint" }
    $" $ {command}"
  }

let years (posts: Post list) =
  Fragment() {
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

let series (series: (Series * int) list) =
  ul () {
    for series, count in series do
      li () {
        a (href = $"/series/{series.Slug}/") { $"{series.Slug}/" }
        span () { $"""{count} {if count = 1 then "post" else "posts"} · {series.Excerpt}""" }
      }
  }
