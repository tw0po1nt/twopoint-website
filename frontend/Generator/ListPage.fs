[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.ListPage

open Oxpecker.ViewEngine

[<RequireQualifiedAccess>]
type PostList =
  | Posts
  | Category of Term
  | Series of Series
  | Tag of Term

/// A page of a list. `Number` counts from 1.
type Page =
  { List: PostList
    Number: int
    Count: int
    Posts: Post list }

let seriesUrlPath = "/series/"

let private seriesCategorySlug = "series"

/// The URL path of page 1.
let private basePath (list: PostList) =
  match list with
  | PostList.Posts -> "/posts/"
  | PostList.Category category -> $"/category/{category.Slug}/"
  | PostList.Series series -> $"/series/{series.Slug}/"
  | PostList.Tag tag -> $"/tag/{tag.Slug}/"

let private isSeriesCategory (list: PostList) =
  match list with
  | PostList.Category category -> category.Slug = seriesCategorySlug
  | _ -> false

let private includes (list: PostList) (post: Post) =
  let frontMatter = post.FrontMatter

  match list with
  | PostList.Posts -> true
  | PostList.Category category -> frontMatter.Category.Slug = category.Slug
  | PostList.Series series -> frontMatter.Series |> Option.exists (fun other -> other.Slug = series.Slug)
  | PostList.Tag tag -> frontMatter.Tags |> List.exists (fun other -> other.Slug = tag.Slug)

/// The pages of the list of each post, then of each category, each series, and each tag.
let all (posts: Post list) =
  [ for list in
        [ PostList.Posts
          yield! Blog.categories posts |> List.map PostList.Category
          yield! Blog.series posts |> List.map (fst >> PostList.Series)
          yield! Blog.tags posts |> List.map (fst >> PostList.Tag) ] do
      let pages = posts |> List.filter (includes list) |> Blog.pages

      for index, shown in List.indexed pages do
        { List = list
          Number = index + 1
          Count = pages.Length
          Posts = shown } ]

let urlPath (page: Page) =
  Blog.pageUrlPath (basePath page.List) page.Number

let robots (page: Page) =
  match page.List with
  | PostList.Tag _ -> Layout.Robots.NoIndex
  | list when isSeriesCategory list -> Layout.Robots.NoIndex
  | _ -> Layout.Robots.Index

let seriesRobots = Layout.Robots.Index

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

let private pager (page: Page) =
  let pageUrlPath number =
    Blog.pageUrlPath (basePath page.List) number

  nav(class' = "pager").attr ("aria-label", "Pages") {
    if page.Number > 1 then
      a(class' = "newer", href = pageUrlPath (page.Number - 1)).attr ("rel", "prev") { "← newer" }

    span () { $"page {page.Number} of {page.Count}" }

    if page.Number < page.Count then
      a(class' = "older", href = pageUrlPath (page.Number + 1)).attr ("rel", "next") { "older →" }
  }

let render (config: StageConfig) (assets: Assets) (widgets: Widgets) (ogImages: OgImages) (posts: Post list) (page: Page) =
  let list = page.List
  let command = $"ls -l {(basePath list).TrimStart '/'}"

  let ogImage =
    match list, page.Posts with
    | PostList.Series { Image = Some _ }, post :: _ -> ogImages.ByPost[post.Slug]
    | _ -> assets.DefaultImage

  let layoutPage: Layout.Page =
    { Title = Some(if page.Number = 1 then title list else $"{title list}, page {page.Number}")
      Description = description list
      // `/category/series/` lists the posts of the series pages, so it sends search engines to `/series/`.
      UrlPath = Some(if isSeriesCategory list then seriesUrlPath else urlPath page)
      Robots = robots page
      OgType = Layout.OgType.Website
      OgImage = ogImage
      Current = None
      Banner = None }

  main (class' = "page") {
    pathLine (if page.Number = 1 then command else $"{command} --page={page.Number}")
    h1 () { heading list }
    p (class' = "excerpt") { description list }

    match page.Posts with
    | [] -> ()
    | shown -> section (class' = "log") { PostLog.years shown }

    if page.Count > 1 then
      pager page
  }
  |> Layout.render config assets widgets posts layoutPage

let renderSeries (config: StageConfig) (assets: Assets) (widgets: Widgets) (posts: Post list) =
  let description = "Each series of posts on TwoPoint, the series with the newest post first."

  let page: Layout.Page =
    { Title = Some "Series"
      Description = description
      UrlPath = Some seriesUrlPath
      Robots = seriesRobots
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
