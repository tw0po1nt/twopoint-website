[<RequireQualifiedAccess>]
module TwoPoint.DevHost.Listen

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Connections
open Microsoft.Extensions.Logging
open System
open System.Diagnostics
open System.IO
open System.Threading.Tasks

/// Tries again in the background while another process uses the port, until `timeout`. A SageFs hard reset stops the
/// old session, and releases its port, only after the init script of the new session ends.
/// SageFs adds its reload script only to an app that `RunAsync` starts.
let start (timeout: TimeSpan) (url: string) (routes: WebApplication -> unit) : Task<Result<WebApplication, string>> =
  let clock = Stopwatch.StartNew()

  let rec attempt () : Task<Result<WebApplication, string>> =
    task {
      let builder = WebApplication.CreateSlimBuilder()
      builder.Logging.AddFilter("Microsoft.Extensions.Hosting.Internal.Host", LogLevel.None) |> ignore
      let app = builder.Build()
      routes app
      let started = TaskCompletionSource()
      app.Lifetime.ApplicationStarted.Register(fun () -> started.TrySetResult() |> ignore) |> ignore
      let running = app.RunAsync url
      let! _ = Task.WhenAny(running, started.Task)

      if started.Task.IsCompleted then
        return Ok app
      else
        match running.Exception with
        | null -> return Error "The app stopped before it started."
        | error ->
          match error.InnerException with
          | :? IOException as bind when (bind.InnerException :? AddressInUseException) && clock.Elapsed < timeout ->
            do! Task.Delay 250
            return! attempt ()
          | inner -> return Error inner.Message
    }

  attempt ()
