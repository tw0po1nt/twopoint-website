[<RequireQualifiedAccess>]
module TwoPoint.Site.Render.Links

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

/// One entry for each source with a problem.
let check (sitePaths: Set<string>) (sources: Source list) : FileProblems list =
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
      let problems =
        source.Body.Links
        |> List.choose (fun link ->
          problem source link.Target
          |> Option.map (Problem.error (Place.Body link.Line)))

      if not problems.IsEmpty then
        FileProblems.create source.File source.Body.StartLine problems ]
  |> List.sortBy _.File
