[<RequireQualifiedAccess>]
module TwoPoint.Site.Dev.Server

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.StaticFiles
open System
open System.Threading.Tasks
open TwoPoint.Site.Generator

let private stage = Stage.Local

let private contentTypes = FileExtensionContentTypeProvider()

let private contentType (path: string) =
  match contentTypes.TryGetContentType path with
  | true, value -> value
  | false, _ -> "application/octet-stream"

let private respond (ctx: HttpContext) status path (render: Render) : Task =
  ctx.Response.StatusCode <- status
  ctx.Response.ContentType <- contentType path
  ctx.Response.Body.WriteAsync(ReadOnlyMemory(render ())).AsTask()

let serve (ctx: HttpContext) : Task =
  let map = RenderMap.create stage
  let path = ctx.Request.Path.Value

  match Lookup.find map path, Lookup.find map "/404.html" with
  | Some render, _ -> respond ctx StatusCodes.Status200OK (Lookup.sitePath path) render
  | None, Some notFound -> respond ctx StatusCodes.Status404NotFound "404.html" notFound
  | None, None ->
    ctx.Response.StatusCode <- StatusCodes.Status404NotFound
    Task.CompletedTask

/// Returns at once, with the server on a background task. SageFs adds its reload script only to an app
/// that `RunAsync` or `Run` starts.
let start () =
  let app = WebApplication.CreateSlimBuilder().Build()
  app.MapGet("/{**path}", RequestDelegate serve) |> ignore
  app.RunAsync((Stage.config stage).SiteUrl.GetLeftPart UriPartial.Authority) |> ignore
  app
