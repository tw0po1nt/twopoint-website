[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.Links

let check (sitePaths: Set<string>) (posts: Post list) : PostError list =
  let postAnchors =
    posts
    |> List.map (fun post -> SitePath.ofUrlPath (Post.urlPath post), post.Body.Anchors)
    |> Map.ofList

  let anchorsAt sitePath =
    if sitePaths.Contains sitePath then
      Some(postAnchors.TryFind sitePath |> Option.defaultValue Set.empty)
    else
      None

  let pending (path: string) =
    PendingPages.urlPaths.Contains path && not (sitePaths.Contains(SitePath.ofUrlPath path))

  let exists path = pending path || sitePaths.Contains(SitePath.ofUrlPath path)

  let problem (post: Post) target =
    match target with
    | SamePage anchor when post.Body.Anchors.Contains anchor -> None
    | SamePage anchor -> Some $"The post has no heading with the id '{anchor}'."
    | Page(path, _) when pending path -> None
    | Page(path, _) when not (exists path) && exists $"{path}/" -> Some $"Add the trailing slash: {path}/"
    | Page(path, anchor) ->
      match anchorsAt (SitePath.ofUrlPath path), anchor with
      | None, _ -> Some $"The output has no page at {path}."
      | Some _, None -> None
      | Some anchors, Some anchor when anchors.Contains anchor -> None
      | Some _, Some anchor -> Some $"The page {path} has no heading with the id '{anchor}'."

  [ for post in posts do
      for link in post.Body.Links do
        match problem post link.Target with
        | Some message -> PostError.at post.File link.Line message
        | None -> () ]
  |> List.sortBy (fun error -> error.File, error.Line)
