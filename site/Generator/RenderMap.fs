namespace TwoPoint.Site.Generator

open System.Text

type Output =
  /// A file of the site, at its path from the site root, for example `posts/index.html`.
  | SiteFile of path: string
  | Newsletter of slug: string
  | StageMarker

type Render = unit -> byte array

type RenderMap = Map<Output, Render>

[<RequireQualifiedAccess>]
module RenderMap =

  let private text (render: unit -> string) : Render = fun () -> Encoding.UTF8.GetBytes(render ())

  let create (stage: Stage) : RenderMap =
    let config = Stage.config stage

    Map
      [ SiteFile "robots.txt", text (fun () -> Robots.render config)
        StageMarker, text (fun () -> Stage.name stage) ]

  let sitePaths (map: RenderMap) =
    map
    |> Map.keys
    |> Seq.choose (function
      | SiteFile path -> Some path
      | _ -> None)
    |> Set.ofSeq
