[<RequireQualifiedAccess>]
module TwoPoint.Site.Dev.Server

open TwoPoint.DevHost
open TwoPoint.Site.Generator
open TwoPoint.Site.Render

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.StaticFiles
open System
open System.IO
open System.Threading.Tasks

let private stage = Stage.Local

let private watchedFolders (root: string) =
  [ Path.Combine(root, "frontend", "content"); Path.Combine(root, WidgetManifest.folder) ]

let private contentTypes = FileExtensionContentTypeProvider()

let private contentType (path: string) =
  match contentTypes.TryGetContentType path with
  | true, value -> value
  | false, _ -> "application/octet-stream"

let serve (site: unit -> DevSite) (ctx: HttpContext) : Task =
  match DevSite.reply (site ()) ctx.Request.Path.Value ctx.Request.QueryString.Value with
  | Content(status, path, bytes) ->
    ctx.Response.StatusCode <- status
    ctx.Response.ContentType <- contentType path
    ctx.Response.Body.WriteAsync(ReadOnlyMemory bytes).AsTask()
  | Redirect target ->
    ctx.Response.Redirect(target, true)
    Task.CompletedTask

let private report (site: DevSite) =
  match site.Render with
  | Ok map -> printfn "dev: loaded the site, %d files" map.Count
  | Error errors ->
    eprintfn "dev: the site has %d errors" errors.Length

    for error in errors do
      eprintfn "  %s" error

let private findRoot () =
  match Site.root (Directory.GetCurrentDirectory()) with
  | Ok root -> root
  | Error message -> failwith message

let startWidgetWatch () =
  match Watch.npmWatch (Path.Combine(findRoot (), "frontend", "Widgets")) with
  | Ok watch -> Some watch
  | Error message ->
    eprintfn "dev: the widget watch did not start: %s" message
    eprintfn "  Run 'npm run watch' in frontend/Widgets, or put npm and node on the PATH of the SageFs daemon."
    None

let start () =
  let root = findRoot ()

  let images =
    ImageFetch.create ()
    |> ImageCache.disk (Path.Combine(root, ImageCache.folder))
    |> Images.ofFetch
    |> ImageCache.memoize

  let site = ref DevSite.empty
  let reloads = Watch.Reloads()

  let load () =
    site.Value <-
      DevSite.load site.Value stage DateTimeOffset.UtcNow (fun () -> Site.loadWidgets root) (fun () -> Site.loadPulledContent images stage root)
    report site.Value
    reloads.Send()

  load ()
  let watch = Watch.folders (TimeSpan.FromMilliseconds 200.0) (watchedFolders root) load

  let routes (app: WebApplication) =
    app.MapGet(Reload.path, RequestDelegate(Reload.events reloads)) |> ignore
    app.MapGet("/{**path}", RequestDelegate(serve (fun () -> site.Value))) |> ignore

  let url = (Stage.config stage).SiteUrl.GetLeftPart UriPartial.Authority

  task {
    match! Listen.start (TimeSpan.FromSeconds 30.0) url routes with
    | Ok app ->
      app.Lifetime.ApplicationStopping.Register(fun () -> watch.Dispose()) |> ignore
      return Some app
    | Error message ->
      watch.Dispose()
      eprintfn "dev: the server did not start on %s: %s" url message
      return None
  }
