[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.Links

/// `pages` maps each site path of the output to the heading ids of that file.
let check (pages: Map<string, Set<string>>) (posts: Post list) : PostError list =
  let converted = posts |> List.map _.Slug |> Set.ofList

  let pending (path: string) =
    PendingPosts.slugs
    |> Set.exists (fun slug -> not (converted.Contains slug) && path = $"/posts/{slug}/")

  let exists path = pending path || pages.ContainsKey(SitePath.ofUrlPath path)

  let problem (post: Post) target =
    match target with
    | SamePage anchor when post.Body.Anchors.Contains anchor -> None
    | SamePage anchor -> Some $"The post has no heading with the id '{anchor}'."
    | Page(path, _) when pending path -> None
    | Page(path, _) when not (exists path) && exists $"{path}/" -> Some $"Add the trailing slash: {path}/"
    | Page(path, anchor) ->
      match pages.TryFind(SitePath.ofUrlPath path), anchor with
      | None, _ -> Some $"The output has no page at {path}."
      | Some _, None -> None
      | Some anchors, Some anchor when anchors.Contains anchor -> None
      | Some _, Some anchor -> Some $"The page {path} has no heading with the id '{anchor}'."

  [ for post in posts do
      for link in post.Body.Links do
        match problem post link.Target with
        | Some message ->
          { File = post.File
            Line = link.Line
            Message = message }
        | None -> () ]
