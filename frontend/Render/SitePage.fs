namespace TwoPoint.Site.Render

[<RequireQualifiedAccess>]
type PostList =
  | Posts
  | Category of Term
  | Series of Series
  | Tag of Term

/// A page of a post list. `Number` counts from 1.
type PostListPage =
  { List: PostList
    Number: int
    Count: int
    Posts: Post list }

[<RequireQualifiedAccess>]
type SitePage =
  | Home
  | Subscribe
  | Unsubscribe
  /// The stop page of the reply emails of one comment.
  | StopReplyEmails
  | About
  | Privacy
  | NotFound
  /// The list of each series.
  | SeriesList
  | PostList of PostListPage
  | Post of Post

/// The files of the site that are not pages, by URL path.
[<RequireQualifiedAccess>]
module FixedFile =

  let robots = "/robots.txt"

  let feed = "/rss.xml"

  let sitemapIndex = "/sitemap-index.xml"

  let sitemap = "/sitemap-0.xml"

  let all = [ robots; feed; sitemapIndex; sitemap ]

[<RequireQualifiedAccess>]
module PostList =

  /// The URL path of page 1.
  let basePath (list: PostList) =
    match list with
    | PostList.Posts -> "/posts/"
    | PostList.Category category -> $"/category/{category.Slug}/"
    | PostList.Series series -> $"/series/{series.Slug}/"
    | PostList.Tag tag -> $"/tag/{tag.Slug}/"

  let private includes (list: PostList) (post: Post) =
    let frontMatter = post.FrontMatter

    match list with
    | PostList.Posts -> true
    | PostList.Category category -> frontMatter.Category.Slug = category.Slug
    | PostList.Series series -> frontMatter.Series |> Option.exists (fun other -> other.Slug = series.Slug)
    | PostList.Tag tag -> frontMatter.Tags |> List.exists (fun other -> other.Slug = tag.Slug)

  /// The pages of the list of each post, then of each category, each series, and each tag.
  let pages (posts: Post list) =
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

  let urlPath (page: PostListPage) =
    Blog.pageUrlPath (basePath page.List) page.Number

[<RequireQualifiedAccess>]
module SitePage =

  /// The fixed pages, then the pages of each post list, then the page of each post.
  let all (posts: Post list) =
    [ SitePage.Home
      SitePage.Subscribe
      SitePage.Unsubscribe
      SitePage.StopReplyEmails
      SitePage.About
      SitePage.Privacy
      SitePage.NotFound
      SitePage.SeriesList
      yield! PostList.pages posts |> List.map SitePage.PostList
      yield! posts |> List.map SitePage.Post ]

  let urlPath (page: SitePage) =
    match page with
    | SitePage.Home -> "/"
    | SitePage.Subscribe -> "/subscribe/"
    | SitePage.Unsubscribe -> "/unsubscribe/"
    | SitePage.StopReplyEmails -> "/comments/stop/"
    | SitePage.About -> "/about/"
    | SitePage.Privacy -> Privacy.urlPath
    | SitePage.NotFound -> "/404.html"
    | SitePage.SeriesList -> "/series/"
    | SitePage.PostList page -> PostList.urlPath page
    | SitePage.Post post -> Post.urlPath post
