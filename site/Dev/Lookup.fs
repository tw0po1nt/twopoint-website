[<RequireQualifiedAccess>]
module TwoPoint.Site.Dev.Lookup

open TwoPoint.Site.Generator

let find (map: RenderMap) (requestPath: string) : (string * Render) option =
  let path = SitePath.ofUrlPath requestPath
  Map.tryFind (SiteFile path) map |> Option.map (fun render -> path, render)

/// The folder path of a request path with no `/` at the end, when the folder has an `index.html`.
/// GitHub Pages sends a `301` to this path, for example from `/unsubscribe` to `/unsubscribe/`.
let folder (map: RenderMap) (requestPath: string) : string option =
  let urlPath = requestPath + "/"

  if requestPath.EndsWith '/' || not (Map.containsKey (SiteFile(SitePath.ofUrlPath urlPath)) map) then
    None
  else
    Some urlPath
