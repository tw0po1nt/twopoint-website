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
    Assets: Assets
    OgImages: OgImages }

[<RequireQualifiedAccess>]
module RenderMap =

  let private text (render: unit -> string) : Render = fun () -> Encoding.UTF8.GetBytes(render ())

  let private postFile (post: Post) = SiteFile(SitePath.ofUrlPath (Post.urlPath post))

  /// `buildTime` decides which post the upcoming post banner shows.
  let create (stage: Stage) (buildTime: DateTimeOffset) (widgets: Widgets) (content: Content) : RenderMap =
    let config = Stage.config stage

    Map
      [ SiteFile "robots.txt", text (fun () -> Robots.render config)
        StageMarker, text (fun () -> Stage.name stage)
        SiteFile(SitePath.ofUrlPath HomePage.urlPath),
        (fun () -> HomePage.render config content.Assets widgets buildTime content.Posts)
        SiteFile(SitePath.ofUrlPath SubscribePage.urlPath),
        (fun () -> SubscribePage.render config content.Assets widgets content.Posts)
        for files in [ content.Assets.Files; content.OgImages.Files; widgets.Files ] do
          for KeyValue(path, bytes) in files do
            SiteFile path, (fun () -> bytes)
        for post in content.Posts do
          postFile post,
          (fun () -> PostPage.render config content.Assets widgets content.Posts content.OgImages.ByPost[post.Slug] post) ]

  let sitePaths (map: RenderMap) =
    map
    |> Map.keys
    |> Seq.choose (function
      | SiteFile path -> Some path
      | _ -> None)
    |> Set.ofSeq
