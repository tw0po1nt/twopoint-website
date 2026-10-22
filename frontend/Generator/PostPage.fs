[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.PostPage

open TwoPoint.Site.Render
open TwoPoint.Site.Widgets

open Oxpecker.ViewEngine
open System

let private pathLine (post: Post) (position: Blog.SeriesPosition option) =
  p (class' = "path") {
    a (href = "/") { "~/twopoint" }
    "/"

    match position with
    | Some position ->
      a (href = $"/series/{position.Series.Slug}/") { $"series/{position.Series.Slug}" }
      $"/%02d{position.Position}"
    | None ->
      let category = post.FrontMatter.Category
      a (href = $"/category/{category.Slug}/") { $"category/{category.Slug}" }
      $"/{post.Slug}"
  }

let private shareLinks (config: StageConfig) (post: Post) =
  let url = Uri.EscapeDataString(Stage.url config (Post.urlPath post))
  let text = Uri.EscapeDataString post.FrontMatter.Title

  let newTab =
    [ "x", $"https://twitter.com/intent/tweet?url={url}&text={text}"
      "facebook", $"https://www.facebook.com/sharer.php?u={url}"
      "linkedin", $"https://www.linkedin.com/shareArticle?mini=true&url={url}&title={text}"
      "whatsapp", $"https://wa.me/?text={text}%%20{url}" ]

  p (class' = "share") {
    "share:"

    for name, href in newTab do
      " "
      a (href = href, target = "_blank", rel = "noopener noreferrer") { name }

    " "
    a (href = $"mailto:?subject={text}&body={text}%%20{url}") { "email" }
  }

let private card (label: string) (empty: string) (post: Post option) : HtmlElement =
  match post with
  | Some post ->
    a (class' = "card", href = Post.urlPath post) {
      span () { label }
      post.FrontMatter.Title
    }
  | None ->
    p (class' = "card is-empty") {
      span () { label }
      empty
    }

let private olderNewer (neighbors: Blog.Neighbors) =
  nav(class' = "older-newer").attr ("aria-label", "Older and newer posts") {
    card "← older" "this is the oldest post" neighbors.Older
    card "newer →" "this is the newest post" neighbors.Newer
  }

let private relatedPosts (related: Post list) =
  Fragment() {
    match related with
    | [] -> ()
    | related ->
      p (class' = "label") { "related/" }

      ul (class' = "related") {
        for post in related do
          li () { a (href = Post.urlPath post) { post.FrontMatter.Title } }
      }
  }

/// The head script shows the panel of an upcoming post from the publish date on.
let private commentsPanel (config: StageConfig) (buildTime: DateTimeOffset) (post: Post) =
  let panel = details (class' = "panel", id = "comments")

  let panel =
    if Blog.isUpcoming buildTime post then
      panel.attr("hidden", "").attr (HideAt.showAttribute, Layout.scriptTime post.FrontMatter.PublishDate)
    else
      panel

  panel {
    summary () { "$ comments --list" }
    Mount.comments config post.Slug
  }

let robots = Layout.Robots.Index

let render
  (config: StageConfig)
  (assets: Assets)
  (widgets: Widgets)
  (buildTime: DateTimeOffset)
  (posts: Post list)
  (ogImage: SizedImage)
  (post: Post)
  =
  let frontMatter = post.FrontMatter
  let position = Blog.seriesPosition posts post

  let page: Layout.Page =
    { Title = Some frontMatter.Title
      Description = frontMatter.Excerpt
      UrlPath = Some(Post.urlPath post)
      Robots = robots
      OgType = Layout.OgType.Article
      OgImage = ogImage
      Current = Some post
      Banner = None }

  main (class' = "post-page") {
    article (class' = "post") {
      pathLine post position
      Article.content post position
      shareLinks config post
      olderNewer (Blog.neighbors posts post)

      commentsPanel config buildTime post

      relatedPosts (Blog.related posts post)
    }

    Article.outline post
  }
  |> Layout.render config assets widgets posts page
