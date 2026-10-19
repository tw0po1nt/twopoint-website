[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.ListPage

open Oxpecker.ViewEngine

[<RequireQualifiedAccess>]
type PostList =
  | Posts
  | Category of Term
  | Series of Series
  | Tag of Term

let seriesUrlPath = "/series/"

/// The URL path of page 1.
let basePath (list: PostList) =
  match list with
  | PostList.Posts -> "/posts/"
  | PostList.Category category -> $"/category/{category.Slug}/"
  | PostList.Series series -> $"/series/{series.Slug}/"
  | PostList.Tag tag -> $"/tag/{tag.Slug}/"

/// `/category/series/` lists the posts of the series pages, so it sends search engines to `/series/`.
let private isSeriesCategory (list: PostList) =
  match list with
  | PostList.Category category -> category.Slug = "series"
  | _ -> false

let private includes (list: PostList) (post: Post) =
  let frontMatter = post.FrontMatter

  match list with
  | PostList.Posts -> true
  | PostList.Category category -> frontMatter.Category.Slug = category.Slug
  | PostList.Series series -> frontMatter.Series |> Option.exists (fun other -> other.Slug = series.Slug)
  | PostList.Tag tag -> frontMatter.Tags |> List.exists (fun other -> other.Slug = tag.Slug)

/// Each page of `list`, page 1 first.
let pages (posts: Post list) (list: PostList) = posts |> List.filter (includes list) |> Blog.pages

/// The list of each post, then each category, each series, and each tag.
let all (posts: Post list) =
  [ PostList.Posts
    yield! Blog.categories posts |> List.map PostList.Category
    yield! Blog.series posts |> List.map (fst >> PostList.Series)
    yield! Blog.tags posts |> List.map (fst >> PostList.Tag) ]

let private heading (list: PostList) =
  match list with
  | PostList.Posts -> "Posts"
  | PostList.Category category -> category.Title
  | PostList.Series series -> series.Title
  | PostList.Tag tag -> $"#{tag.Title}"

let private title (list: PostList) =
  match list with
  | PostList.Posts -> "Posts"
  | PostList.Category category -> $"Category '{category.Title}'"
  | PostList.Series series -> $"Series '{series.Title}'"
  | PostList.Tag tag -> $"Posts by tag '{tag.Title}'"

let private description (list: PostList) =
  match list with
  | PostList.Posts -> "Each post on TwoPoint, the newest first."
  | PostList.Category category -> $"Each post in the category {category.Title}, the newest first."
  | PostList.Series series -> series.Excerpt
  | PostList.Tag tag -> $"Each post with the tag {tag.Title}, the newest first."

let private pathLine (command: string) =
  p (class' = "path") {
    a (href = "/") { "~/twopoint" }
    $" $ {command}"
  }

let private pager (basePath: string) (number: int) (count: int) =
  nav(class' = "pager").attr ("aria-label", "Pages") {
    if number > 1 then
      a(class' = "newer", href = Blog.pageUrlPath basePath (number - 1)).attr ("rel", "prev") { "← newer" }

    span () { $"page {number} of {count}" }

    if number < count then
      a(class' = "older", href = Blog.pageUrlPath basePath (number + 1)).attr ("rel", "next") { "older →" }
  }

/// `number` counts from 1.
let render (config: StageConfig) (assets: Assets) (widgets: Widgets) (ogImages: OgImages) (posts: Post list) (list: PostList) (number: int) =
  let pages = pages posts list
  let shown = pages[number - 1]
  let basePath = basePath list
  let urlPath = Blog.pageUrlPath basePath number

  let ogImage =
    match list, shown with
    | PostList.Series { Image = Some _ }, post :: _ -> ogImages.ByPost[post.Slug]
    | _ -> assets.DefaultImage

  let page: Layout.Page =
    { Title = Some(if number = 1 then title list else $"{title list}, page {number}")
      Description = description list
      UrlPath = Some(if isSeriesCategory list then seriesUrlPath else urlPath)
      Robots = if isSeriesCategory list then Layout.Robots.NoIndex else Layout.Robots.Index
      OgType = Layout.OgType.Website
      OgImage = ogImage
      Current = None
      Banner = None }

  main (class' = "page") {
    pathLine (if number = 1 then $"ls -l {basePath.TrimStart '/'}" else $"ls -l {basePath.TrimStart '/'} --page={number}")
    h1 () { heading list }
    p (class' = "excerpt") { description list }

    match shown with
    | [] -> ()
    | shown -> section (class' = "log") { PostLog.years shown }

    if pages.Length > 1 then
      pager basePath number pages.Length
  }
  |> Layout.render config assets widgets posts page

let renderSeries (config: StageConfig) (assets: Assets) (widgets: Widgets) (posts: Post list) =
  let description = "Each series of posts on TwoPoint, the series with the newest post first."

  let page: Layout.Page =
    { Title = Some "Series"
      Description = description
      UrlPath = Some seriesUrlPath
      Robots = Layout.Robots.Index
      OgType = Layout.OgType.Website
      OgImage = assets.DefaultImage
      Current = None
      Banner = None }

  main (class' = "page") {
    pathLine "ls series/"
    h1 () { "Series" }
    p (class' = "excerpt") { description }

    match Blog.series posts with
    | [] -> ()
    | series -> section (class' = "series-list") { PostLog.series series }
  }
  |> Layout.render config assets widgets posts page
