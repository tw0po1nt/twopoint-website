[<RequireQualifiedAccess>]
module TwoPoint.Site.Dev.Lookup

open TwoPoint.Site.Generator

let find (map: RenderMap) (requestPath: string) : (string * Render) option =
  let path = SitePath.ofUrlPath requestPath
  Map.tryFind (SiteFile path) map |> Option.map (fun render -> path, render)
