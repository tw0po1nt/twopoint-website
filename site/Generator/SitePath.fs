[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.SitePath

/// The URL path is decoded, for example `/posts/`. A folder path gives its `index.html`.
let ofUrlPath (urlPath: string) =
  let path = urlPath.TrimStart '/'
  if path = "" || path.EndsWith '/' then path + "index.html" else path
