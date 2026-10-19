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

let private fetch = ImageFetch.create ()

let private contentTypes = FileExtensionContentTypeProvider()

let private contentType (path: string) =
  match contentTypes.TryGetContentType path with
  | true, value -> value
  | false, _ -> "application/octet-stream"

let private respond (ctx: HttpContext) status path (render: Render) : Task =
  ctx.Response.StatusCode <- status
  ctx.Response.ContentType <- contentType path
  ctx.Response.Body.WriteAsync(ReadOnlyMemory(render ())).AsTask()

let private serveMap (ctx: HttpContext) (map: RenderMap) : Task =
  match Lookup.find map ctx.Request.Path.Value, Lookup.folder map ctx.Request.Path.Value with
  | Some (path, render), _ -> respond ctx StatusCodes.Status200OK path render
  | None, Some folder ->
    ctx.Response.Redirect(folder + ctx.Request.QueryString.Value, true)
    Task.CompletedTask
  | None, None ->
    match Lookup.find map "/404.html" with
    | Some (path, notFound) -> respond ctx StatusCodes.Status404NotFound path notFound
    | None ->
      ctx.Response.StatusCode <- StatusCodes.Status404NotFound
      Task.CompletedTask

let private serveErrors (ctx: HttpContext) (errors: string list) : Task =
  ctx.Response.StatusCode <- StatusCodes.Status500InternalServerError
  ctx.Response.ContentType <- "text/plain; charset=utf-8"
  ctx.Response.WriteAsync(String.concat "\n" errors)

let serve (ctx: HttpContext) : Task =
  let map =
    Site.root (Directory.GetCurrentDirectory())
    |> Result.mapError List.singleton
    |> Result.bind (fun root -> Site.load fetch root stage DateTimeOffset.UtcNow)

  match map with
  | Ok map -> serveMap ctx map
  | Error errors -> serveErrors ctx errors

/// SageFs adds its reload script only to an app that `RunAsync` or `Run` starts.
let start () =
  let app = WebApplication.CreateSlimBuilder().Build()
  app.MapGet("/{**path}", RequestDelegate serve) |> ignore
  app.RunAsync((Stage.config stage).SiteUrl.GetLeftPart UriPartial.Authority) |> ignore
  app
