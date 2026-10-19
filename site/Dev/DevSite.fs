namespace TwoPoint.Site.Dev

open TwoPoint.Site.Generator

open FsToolkit.ErrorHandling
open System
open System.Text

type DevSite =
  { Map: Result<RenderMap, string list>
    /// From the last widget load that worked, so that the error page can still reload.
    DevFiles: Map<string, byte array>
    /// The URL path of the dev reload script, from the last widget load that worked.
    ReloadScript: string option }

/// `path` is a site path, which gives the content type.
type Reply =
  | Content of status: int * path: string * bytes: byte array
  | Redirect of target: string

[<RequireQualifiedAccess>]
module DevSite =

  let empty =
    { Map = Error []
      DevFiles = Map.empty
      ReloadScript = None }

  let create
    (previous: DevSite)
    (stage: Stage)
    (buildTime: DateTimeOffset)
    (widgets: Result<Widgets, string list>)
    (content: Result<Content, string list>)
    : DevSite =
    let map =
      validation {
        let! widgets = widgets
        and! content = content
        return widgets, content
      }
      |> Result.bind (fun (widgets, content) -> Site.renderMap stage buildTime widgets content)

    match widgets with
    | Ok widgets ->
      { Map = map
        DevFiles = widgets.DevFiles
        ReloadScript = Some $"/{Widgets.sitePath widgets.Manifest.DevReload}" }
    | Error _ -> { previous with Map = map }

  let withReloadScript (src: string) (html: byte array) =
    let text = Encoding.UTF8.GetString html
    let tag = $"<script src=\"{src}\"></script>"
    let bodyEnd = text.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase)
    Encoding.UTF8.GetBytes(if bodyEnd < 0 then text + tag else text.Insert(bodyEnd, tag))

  /// Each request gets the error page while the site has errors, but a dev file still loads.
  let reply (site: DevSite) (requestPath: string) (queryString: string) : Reply =
    let content status (path: string) bytes =
      match site.ReloadScript with
      | Some src when path.EndsWith ".html" -> Content(status, path, withReloadScript src bytes)
      | _ -> Content(status, path, bytes)

    let path = SitePath.ofUrlPath requestPath

    match site.DevFiles.TryFind path, site.Map with
    | Some bytes, _ -> Content(200, path, bytes)
    | None, Error errors -> content 500 "__dev/error.html" (ErrorPage.render errors)
    | None, Ok map ->
      match Lookup.find map requestPath, Lookup.trailingSlashRedirect map requestPath queryString with
      | Some(path, render), _ -> content 200 path (render ())
      | None, Some target -> Redirect target
      | None, None ->
        match Lookup.find map "/404.html" with
        | Some(path, notFound) -> content 404 path (notFound ())
        | None -> Content(404, path, [||])
