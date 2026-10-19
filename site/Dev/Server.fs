[<RequireQualifiedAccess>]
module TwoPoint.Site.Dev.Server

open TwoPoint.Site.Generator

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.StaticFiles
open System
open System.IO
open System.Threading.Tasks

let private stage = Stage.Local

let reloadPath = "/__dev/reload"

let private watchedFolders (root: string) =
  [ Path.Combine(root, "site", "content"); Path.Combine(root, WidgetManifest.folder) ]

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

let events (reloads: Watch.Reloads) (ctx: HttpContext) : Task =
  task {
    ctx.Response.ContentType <- "text/event-stream"
    ctx.Response.Headers.CacheControl <- "no-cache"
    do! ctx.Response.Body.FlushAsync ctx.RequestAborted

    try
      while not ctx.RequestAborted.IsCancellationRequested do
        do! reloads.Next.WaitAsync ctx.RequestAborted
        do! ctx.Response.WriteAsync("data: reload\n\n", ctx.RequestAborted)
        do! ctx.Response.Body.FlushAsync ctx.RequestAborted
    with :? OperationCanceledException ->
      ()
  }
  :> Task

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
  Watch.widgets (Path.Combine(findRoot (), "site", "Widgets"))

/// SageFs adds its reload script only to an app that `RunAsync` or `Run` starts.
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
      DevSite.load site.Value stage DateTimeOffset.UtcNow (fun () -> Site.loadWidgets root) (fun () -> Site.loadContent images root)
    report site.Value
    reloads.Send()

  load ()
  let watch = Watch.folders (TimeSpan.FromMilliseconds 200.0) (watchedFolders root) load

  let app = WebApplication.CreateSlimBuilder().Build()
  app.MapGet(reloadPath, RequestDelegate(events reloads)) |> ignore
  app.MapGet("/{**path}", RequestDelegate(serve (fun () -> site.Value))) |> ignore
  app.Lifetime.ApplicationStopping.Register(fun () -> watch.Dispose()) |> ignore
  app.RunAsync((Stage.config stage).SiteUrl.GetLeftPart UriPartial.Authority) |> ignore
  app
