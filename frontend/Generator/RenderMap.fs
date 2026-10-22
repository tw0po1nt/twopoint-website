namespace TwoPoint.Site.Generator

open TwoPoint.Site.Render

open System
open System.Text

type Output =
  /// A file of the site, at its path from the site root, for example `posts/index.html`.
  | SiteFile of path: string
  | Newsletter of slug: string
  | StageMarker

type Render = unit -> byte array

type RenderMap = Map<Output, Render>

type Content =
  { Posts: Post list
    Privacy: MarkdownPage
    Assets: Assets
    OgImages: OgImages
    Email: EmailStyles
    /// The problems of the posts and the privacy page, when no problem is an error.
    Warnings: FileProblems list }

[<RequireQualifiedAccess>]
module RenderMap =

  let private text (render: unit -> string) : Render = fun () -> Encoding.UTF8.GetBytes(render ())

  let private file (urlPath: string) = SiteFile(SitePath.ofUrlPath urlPath)

  let private pageEntry (config: StageConfig) (buildTime: DateTimeOffset) (widgets: Widgets) (content: Content) (sitePage: SitePage) =
    let assets = content.Assets
    let posts = content.Posts

    match sitePage with
    | SitePage.Home -> HomePage.robots, (fun () -> HomePage.render config assets widgets buildTime posts)
    | SitePage.Subscribe -> SubscribePage.robots, (fun () -> SubscribePage.render config assets widgets posts)
    | SitePage.Unsubscribe -> UnsubscribePage.robots, (fun () -> UnsubscribePage.render config assets widgets posts)
    | SitePage.About -> AboutPage.robots, (fun () -> AboutPage.render config assets widgets posts)
    | SitePage.Privacy -> PrivacyPage.robots, (fun () -> PrivacyPage.render config assets widgets posts content.Privacy)
    | SitePage.NotFound -> NotFoundPage.robots, (fun () -> NotFoundPage.render config assets widgets posts)
    | SitePage.SeriesList -> ListPage.seriesRobots, (fun () -> ListPage.renderSeries config assets widgets posts)
    | SitePage.PostList list ->
      ListPage.robots list, (fun () -> ListPage.render config assets widgets content.OgImages posts list)
    | SitePage.Post post ->
      PostPage.robots,
      (fun () -> PostPage.render config assets widgets buildTime posts content.OgImages.ByPost[post.Slug] post)

  let private pages (config: StageConfig) (buildTime: DateTimeOffset) (widgets: Widgets) (content: Content) =
    [ for sitePage in SitePage.all content.Posts do
        let robots, render = pageEntry config buildTime widgets content sitePage
        SitePage.urlPath sitePage, robots, render ]

  /// `buildTime` decides which post the upcoming post banner shows, and which Comments panels are hidden.
  let create (stage: Stage) (buildTime: DateTimeOffset) (widgets: Widgets) (content: Content) : RenderMap =
    let config = Stage.config stage
    let content = { content with Posts = Blog.withNewestTermTitles content.Posts }
    let pages = pages config buildTime widgets content

    Map
      [ file FixedFile.robots, text (fun () -> Robots.render config Sitemap.indexUrlPath)
        StageMarker, text (fun () -> Stage.name stage)
        file Feed.urlPath, (fun () -> Feed.render config content.Posts)
        file Sitemap.indexUrlPath, (fun () -> Sitemap.renderIndex config)
        file Sitemap.urlPath, (fun () -> Sitemap.render config [ for urlPath, robots, _ in pages -> urlPath, robots ])
        for urlPath, _, render in pages do
          file urlPath, render
        for post in content.Posts do
          Newsletter post.Slug, (fun () -> Newsletter.render config content.Email post)
        for files in [ content.Assets.Files; content.OgImages.Files; widgets.Files ] do
          for KeyValue(path, bytes) in files do
            SiteFile path, (fun () -> bytes) ]

  let sitePaths (map: RenderMap) =
    map
    |> Map.keys
    |> Seq.choose (function
      | SiteFile path -> Some path
      | _ -> None)
    |> Set.ofSeq

  let newsletterSlugs (map: RenderMap) : string list =
    map
    |> Map.keys
    |> Seq.choose (function
      | Newsletter slug -> Some slug
      | _ -> None)
    |> List.ofSeq
