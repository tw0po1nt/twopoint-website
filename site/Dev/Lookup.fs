[<RequireQualifiedAccess>]
module TwoPoint.Site.Dev.Lookup

open TwoPoint.Site.Generator

open Oxpecker.ViewEngine

let indexPath = "/__dev/"

let private newsletterPrefix = "/__dev/newsletter/"

let newsletterPath (slug: string) = $"{newsletterPrefix}{slug}/"

let private newsletterSlugs (map: RenderMap) =
  map
  |> Map.keys
  |> Seq.choose (function
    | Newsletter slug -> Some slug
    | _ -> None)
  |> List.ofSeq

let private index (slugs: string list) : Render =
  fun () ->
    html (lang = "en") {
      head () {
        meta (charset = "utf-8")
        title () { "Newsletters" }
      }

      body () {
        h1 () { "Newsletters" }

        ul () {
          for slug in slugs do
            li () { a (href = newsletterPath slug) { slug } }
        }
      }
    }
    |> Render.toHtmlDocBytes

/// The site path gives the content type, so each dev page gets a path that ends in `index.html`.
let find (map: RenderMap) (requestPath: string) : (string * Render) option =
  if requestPath = indexPath then
    Some("__dev/index.html", index (newsletterSlugs map))
  elif requestPath.StartsWith newsletterPrefix && requestPath.EndsWith '/' then
    let slug = requestPath.Substring(newsletterPrefix.Length).TrimEnd '/'

    Map.tryFind (Newsletter slug) map
    |> Option.map (fun render -> SitePath.ofUrlPath (newsletterPath slug), render)
  else
    let path = SitePath.ofUrlPath requestPath
    Map.tryFind (SiteFile path) map |> Option.map (fun render -> path, render)

/// GitHub Pages sends a `301` to this target, for example from `/unsubscribe?a=b` to `/unsubscribe/?a=b`.
let trailingSlashRedirect (map: RenderMap) (requestPath: string) (queryString: string) : string option =
  let urlPath = requestPath + "/"

  if requestPath.EndsWith '/' || (find map urlPath).IsNone then
    None
  else
    Some(urlPath + queryString)
