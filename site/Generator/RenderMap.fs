namespace TwoPoint.Site.Generator

open System.Text

/// One file that the build writes.
type Output =
  /// A file of the site, at its path from the site root, for example `posts/index.html`.
  | SiteFile of path: string
  /// The newsletter of the post with this slug.
  | Newsletter of slug: string
  /// The file that contains the stage name of the build.
  | StageMarker

/// A render that runs only when a caller asks for its bytes.
type Render = unit -> byte array

type RenderMap = Map<Output, Render>

[<RequireQualifiedAccess>]
module RenderMap =

  let private text (render: unit -> string) : Render = fun () -> Encoding.UTF8.GetBytes(render ())

  /// Gives each output of the build for the stage.
  let create (stage: Stage) : RenderMap =
    let config = Stage.config stage

    Map
      [ SiteFile "robots.txt", text (fun () -> Robots.render config)
        StageMarker, text (fun () -> Stage.name stage) ]

  /// The paths of the site files, for example `robots.txt`.
  let sitePaths (map: RenderMap) =
    map
    |> Map.keys
    |> Seq.choose (function
      | SiteFile path -> Some path
      | _ -> None)
    |> Set.ofSeq
