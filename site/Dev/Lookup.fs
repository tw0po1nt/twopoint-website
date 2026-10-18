[<RequireQualifiedAccess>]
module TwoPoint.Site.Dev.Lookup

open TwoPoint.Site.Generator

let sitePath (requestPath: string) = SitePath.ofUrlPath requestPath

let find (map: RenderMap) (requestPath: string) : (string * Render) option =
  let path = sitePath requestPath
  Map.tryFind (SiteFile path) map |> Option.map (fun render -> path, render)
