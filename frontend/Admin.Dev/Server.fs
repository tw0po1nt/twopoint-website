[<RequireQualifiedAccess>]
module TwoPoint.Admin.Dev.Server

open TwoPoint.DevHost

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Primitives
open System
open System.IO
open System.Text.Json
open System.Threading.Tasks

let private admin = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "Admin"))

let private dist = Path.Combine(admin, "dist")

/// A site path that leaves `root` gives `Missing`.
let find (root: string) (sitePath: string) : Entry =
  let root = Path.GetFullPath root
  let full = Path.GetFullPath(Path.Combine(root, sitePath.TrimStart '/'))

  if full <> root && not (full.StartsWith(root + string Path.DirectorySeparatorChar)) then Missing
  elif Directory.Exists full then Folder
  elif File.Exists full then Found(File.ReadAllBytes full)
  else Missing

let serve (config: StaticWebAppConfig) (root: string) (ctx: HttpContext) : Task =
  let reply = StaticWebApp.reply config (find root) ctx.Request.Path.Value
  ctx.Response.StatusCode <- reply.Status

  for KeyValue(name, value) in reply.Headers do
    ctx.Response.Headers[name] <- StringValues value

  ctx.Response.Body.WriteAsync(ReadOnlyMemory reply.Body).AsTask()

let private origin () =
  use document = JsonDocument.Parse(File.ReadAllText(Path.Combine(admin, "config.json")))
  document.RootElement.GetProperty("dev").GetProperty("origin").GetString()

let startWatch () =
  match Watch.npmWatch admin with
  | Ok watch -> Some watch
  | Error message ->
    eprintfn "admin dev: the admin watch did not start: %s" message
    eprintfn "  Run 'npm run watch' in frontend/Admin, or put npm and node on the PATH of the SageFs daemon."
    None

let start () =
  let config =
    match StaticWebApp.parse (File.ReadAllText(Path.Combine(admin, "staticwebapp.config.json"))) with
    | Ok config -> config
    | Error message -> failwith message

  let reloads = Watch.Reloads()
  let watch = Watch.folders (TimeSpan.FromMilliseconds 200.0) [ dist ] reloads.Send

  let routes (app: WebApplication) =
    app.MapGet(Reload.path, RequestDelegate(Reload.events reloads)) |> ignore
    app.MapGet("/{**path}", RequestDelegate(serve config dist)) |> ignore

  let url = origin ()

  task {
    let! result = Listen.serve "admin dev" url routes watch

    if Result.isOk result then
      printfn "admin dev: serves %s on %s" dist url

    return result
  }
