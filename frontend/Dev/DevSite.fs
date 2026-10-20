namespace TwoPoint.Site.Dev

open TwoPoint.Site.Generator

open FsToolkit.ErrorHandling
open System
open System.Text

type DevSite =
  { Render: Result<RenderMap, string list>
    /// From the last widget load that worked.
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
    { Render = Error []
      DevFiles = Map.empty
      ReloadScript = None }

  let private attempt (load: unit -> Result<'T, string list>) =
    try
      load ()
    with error ->
      Error [ error.Message ]

  let load
    (previous: DevSite)
    (stage: Stage)
    (buildTime: DateTimeOffset)
    (loadWidgets: unit -> Result<Widgets, string list>)
    (loadContent: unit -> Result<Content, string list>)
    : DevSite =
    let widgets = attempt loadWidgets
    let content = attempt loadContent

    let render =
      validation {
        let! widgets = widgets
        and! content = content
        return widgets, content
      }
      |> Result.bind (fun (widgets, content) -> attempt (fun () -> Site.renderMap stage buildTime widgets content))

    match widgets with
    | Ok widgets ->
      { Render = render
        DevFiles = widgets.DevFiles
        ReloadScript = Some $"/{Widgets.sitePath widgets.Manifest.DevReload}" }
    | Error _ -> { previous with Render = render }

  let withReloadScript (src: string) (html: byte array) =
    let text = Encoding.UTF8.GetString html
    let tag = $"<script src=\"{src}\"></script>"
    let bodyEnd = text.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase)
    Encoding.UTF8.GetBytes(if bodyEnd < 0 then text + tag else text.Insert(bodyEnd, tag))

  let reply (site: DevSite) (requestPath: string) (queryString: string) : Reply =
    let page status (path: string) bytes =
      match site.ReloadScript with
      | Some src when path.EndsWith ".html" -> Content(status, path, withReloadScript src bytes)
      | _ -> Content(status, path, bytes)

    let path = SitePath.ofUrlPath requestPath

    match site.DevFiles.TryFind path, site.Render with
    | Some bytes, _ -> Content(200, path, bytes)
    | None, Error errors -> page 500 "__dev/error.html" (ErrorPage.render errors)
    | None, Ok map ->
      match Lookup.find map requestPath, Lookup.trailingSlashRedirect map requestPath queryString with
      | Some(path, render), _ -> page 200 path (render ())
      | None, Some target -> Redirect target
      | None, None ->
        match Lookup.find map "/404.html" with
        | Some(path, notFound) -> page 404 path (notFound ())
        | None -> Content(404, path, [||])
