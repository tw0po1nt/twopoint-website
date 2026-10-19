[<RequireQualifiedAccess>]
module TwoPoint.Site.Dev.Lookup

open TwoPoint.Site.Generator

let find (map: RenderMap) (requestPath: string) : (string * Render) option =
  let path = SitePath.ofUrlPath requestPath
  Map.tryFind (SiteFile path) map |> Option.map (fun render -> path, render)

/// GitHub Pages sends a `301` to this target, for example from `/unsubscribe?a=b` to `/unsubscribe/?a=b`.
let trailingSlashRedirect (map: RenderMap) (requestPath: string) (queryString: string) : string option =
  let urlPath = requestPath + "/"

  if requestPath.EndsWith '/' || not (Map.containsKey (SiteFile(SitePath.ofUrlPath urlPath)) map) then
    None
  else
    Some(urlPath + queryString)
