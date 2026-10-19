namespace TwoPoint.Site.Generator

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
    Email: EmailStyles }

[<RequireQualifiedAccess>]
module RenderMap =

  let private text (render: unit -> string) : Render = fun () -> Encoding.UTF8.GetBytes(render ())

  let private file (urlPath: string) = SiteFile(SitePath.ofUrlPath urlPath)

  let private pages (config: StageConfig) (buildTime: DateTimeOffset) (widgets: Widgets) (content: Content) =
    [ HomePage.urlPath, HomePage.robots, (fun () -> HomePage.render config content.Assets widgets buildTime content.Posts)
      SubscribePage.urlPath, SubscribePage.robots, (fun () -> SubscribePage.render config content.Assets widgets content.Posts)
      UnsubscribePage.urlPath,
      UnsubscribePage.robots,
      (fun () -> UnsubscribePage.render config content.Assets widgets content.Posts)
      AboutPage.urlPath, AboutPage.robots, (fun () -> AboutPage.render config content.Assets widgets content.Posts)
      PrivacyPage.urlPath,
      PrivacyPage.robots,
      (fun () -> PrivacyPage.render config content.Assets widgets content.Posts content.Privacy)
      $"/{NotFoundPage.sitePath}",
      NotFoundPage.robots,
      (fun () -> NotFoundPage.render config content.Assets widgets content.Posts)
      ListPage.seriesUrlPath,
      ListPage.seriesRobots,
      (fun () -> ListPage.renderSeries config content.Assets widgets content.Posts)
      for page in ListPage.all content.Posts do
        ListPage.urlPath page,
        ListPage.robots page,
        (fun () -> ListPage.render config content.Assets widgets content.OgImages content.Posts page)
      for post in content.Posts do
        Post.urlPath post,
        PostPage.robots,
        (fun () ->
          PostPage.render config content.Assets widgets buildTime content.Posts content.OgImages.ByPost[post.Slug] post) ]

  /// `buildTime` decides which post the upcoming post banner shows, and which Comments panels are hidden.
  let create (stage: Stage) (buildTime: DateTimeOffset) (widgets: Widgets) (content: Content) : RenderMap =
    let config = Stage.config stage
    let pages = pages config buildTime widgets content

    Map
      [ SiteFile "robots.txt", text (fun () -> Robots.render config Sitemap.indexUrlPath)
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
