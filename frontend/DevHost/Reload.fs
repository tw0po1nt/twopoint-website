[<RequireQualifiedAccess>]
module TwoPoint.DevHost.Reload

open Microsoft.AspNetCore.Http
open System
open System.Threading.Tasks

let path = "/__dev/reload"

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
