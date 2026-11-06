[<RequireQualifiedAccess>]
module TwoPoint.Site.Render.SitePath

/// The URL path is decoded, for example `/posts/`. A folder path gives its `index.html`.
let ofUrlPath (urlPath: string) =
  let path = urlPath.TrimStart '/'
  if path = "" || path.EndsWith '/' then path + "index.html" else path

/// The path of each page and each fixed file of the site. The assets, the `og:image` crops, and the widgets are not in the set.
let all (posts: Post list) =
  (SitePage.all posts |> List.map SitePage.urlPath) @ FixedFile.all
  |> List.map ofUrlPath
  |> Set.ofList
