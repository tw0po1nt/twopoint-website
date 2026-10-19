[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.PostPage

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

let private row (term: string) (definition: HtmlElement) =
  div () {
    dt () { term }
    dd () { definition }
  }

let private frontMatterBlock (post: Post) (position: Blog.SeriesPosition option) =
  let frontMatter = post.FrontMatter

  dl (class' = "front-matter") {
    row "date" (time (datetime = Layout.isoTime frontMatter.PublishDate) { Layout.shownDate frontMatter.PublishDate })

    match frontMatter.UpdateDate with
    | Some updated -> row "updated" (time (datetime = Layout.isoTime updated) { Layout.shownDate updated })
    | None -> ()

    row "author" (Fragment() { frontMatter.Author })

    match position with
    | Some position ->
      row
        "series"
        (Fragment() {
          a (href = $"/series/{position.Series.Slug}/") { position.Series.Slug }
          $" ({position.Position} of {position.Count})"
        })
    | None -> ()

    match frontMatter.Tags with
    | [] -> ()
    | tags ->
      row
        "tags"
        (Fragment() {
          for index, tag in List.indexed tags do
            if index > 0 then
              " "

            a (href = $"/tag/{tag.Slug}/") { $"#{tag.Title}" }
        })

    if post.Body.ReadingTime > 0 then
      row "read" (Fragment() { $"{post.Body.ReadingTime} min" })
  }

let private shareLinks (config: StageConfig) (post: Post) =
  let url = Uri.EscapeDataString(Layout.url config (Post.urlPath post))
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

let private outline (post: Post) =
  let headings = post.Body.Headings |> List.filter (fun heading -> heading.Level = 2 || heading.Level = 4)

  aside(class' = "outline").attr ("aria-label", "Outline") {
    match headings with
    | [] -> ()
    | headings ->
      p (class' = "label") { "outline" }

      ul () {
        for heading in headings do
          li (class' = $"outline-h{heading.Level}") { a (href = $"#{heading.Id}") { heading.Text } }
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
      Robots = Layout.Robots.Index
      OgType = Layout.OgType.Article
      OgImage = ogImage
      Current = Some post
      Banner = None }

  main (class' = "post-page") {
    article (class' = "post") {
      pathLine post position
      frontMatterBlock post position
      h1 () { frontMatter.Title }
      p (class' = "excerpt") { frontMatter.Excerpt }
      div (class' = "prose") { raw post.Body.Html }
      shareLinks config post
      olderNewer (Blog.neighbors posts post)

      commentsPanel config buildTime post

      relatedPosts (Blog.related posts post)
    }

    outline post
  }
  |> Layout.render config assets widgets posts page
