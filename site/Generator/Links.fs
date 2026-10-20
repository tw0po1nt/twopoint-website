[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.Links

/// A Markdown file of the site, at the URL path of its page.
type Source = { File: string; UrlPath: string; Body: Body }

let ofPost (post: Post) =
  { File = post.File
    UrlPath = Post.urlPath post
    Body = post.Body }

let ofPage (urlPath: string) (page: MarkdownPage) =
  { File = page.File
    UrlPath = urlPath
    Body = page.Body }

let check (sitePaths: Set<string>) (sources: Source list) : PostError list =
  let anchorsByPath =
    sources
    |> List.map (fun source -> SitePath.ofUrlPath source.UrlPath, source.Body.Anchors)
    |> Map.ofList

  let anchorsAt sitePath =
    if sitePaths.Contains sitePath then
      Some(anchorsByPath.TryFind sitePath |> Option.defaultValue Set.empty)
    else
      None

  let exists path = sitePaths.Contains(SitePath.ofUrlPath path)

  let problem (source: Source) target =
    match target with
    | SamePage anchor when source.Body.Anchors.Contains anchor -> None
    | SamePage anchor -> Some $"The page has no heading with the id '{anchor}'."
    | Page(path, _) when not (exists path) && exists $"{path}/" -> Some $"Add the trailing slash: {path}/"
    | Page(path, anchor) ->
      match anchorsAt (SitePath.ofUrlPath path), anchor with
      | None, _ -> Some $"The output has no page at {path}."
      | Some _, None -> None
      | Some anchors, Some anchor when anchors.Contains anchor -> None
      | Some _, Some anchor -> Some $"The page {path} has no heading with the id '{anchor}'."

  [ for source in sources do
      for link in source.Body.Links do
        match problem source link.Target with
        | Some message -> PostError.at source.File link.Line message
        | None -> () ]
  |> List.sortBy (fun error -> error.File, error.Line)
