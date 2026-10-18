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

  let private postFile (post: Post) = SiteFile(SitePath.ofUrlPath (Post.urlPath post))

  /// Fails with each internal link of the posts that has no path or no anchor in the output.
  let create (stage: Stage) (posts: Post list) : Result<RenderMap, PostError list> =
    let config = Stage.config stage

    let map =
      Map
        [ SiteFile "robots.txt", text (fun () -> Robots.render config)
          StageMarker, text (fun () -> Stage.name stage)
          for post in posts do
            postFile post, (fun () -> PostPage.render config post) ]

    let anchors =
      posts |> List.map (fun post -> postFile post, post.Body.Anchors) |> Map.ofList

    let pages =
      map
      |> Map.toList
      |> List.choose (fun (output, _) ->
        match output with
        | SiteFile path -> Some(path, anchors.TryFind output |> Option.defaultValue Set.empty)
        | _ -> None)
      |> Map.ofList

    match Links.check pages posts with
    | [] -> Ok map
    | errors -> Error(errors |> List.sortBy (fun error -> error.File, error.Line))

  let sitePaths (map: RenderMap) =
    map
    |> Map.keys
    |> Seq.choose (function
      | SiteFile path -> Some path
      | _ -> None)
    |> Set.ofSeq
