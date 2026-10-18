[<RequireQualifiedAccess>]
module TwoPoint.Site.Dev.Lookup

open TwoPoint.Site.Generator

/// The request path is the decoded URL path, for example `/posts/`.
let sitePath (requestPath: string) =
  let path = requestPath.TrimStart '/'
  if path = "" || path.EndsWith '/' then path + "index.html" else path

let find (map: RenderMap) (requestPath: string) : (string * Render) option =
  let path = sitePath requestPath
  Map.tryFind (SiteFile path) map |> Option.map (fun render -> path, render)
