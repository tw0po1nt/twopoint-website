[<RequireQualifiedAccess>]
module TwoPoint.DevHost.Listen

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Connections
open Microsoft.Extensions.Logging
open System
open System.Diagnostics
open System.IO
open System.Threading.Tasks

type NotStarted =
  | PortInUse
  | StoppedBeforeStart
  | Failed of string

/// Tries again in the background while another process uses the port, until `timeout`.
let start (timeout: TimeSpan) (url: string) (routes: WebApplication -> unit) : Task<Result<WebApplication, NotStarted>> =
  let clock = Stopwatch.StartNew()

  let rec attempt () : Task<Result<WebApplication, NotStarted>> =
    task {
      let builder = WebApplication.CreateSlimBuilder()
      builder.Logging.AddFilter("Microsoft.Extensions.Hosting.Internal.Host", LogLevel.None) |> ignore
      let app = builder.Build()
      routes app
      let started = TaskCompletionSource()
      app.Lifetime.ApplicationStarted.Register(fun () -> started.TrySetResult() |> ignore) |> ignore
      // SageFs adds its reload script only to an app that `RunAsync` starts.
      let running = app.RunAsync url
      let! _ = Task.WhenAny(running, started.Task)

      if started.Task.IsCompleted then
        return Ok app
      else
        match running.Exception with
        | null -> return Error StoppedBeforeStart
        | error ->
          match error.InnerException with
          | :? IOException as bind when (bind.InnerException :? AddressInUseException) ->
            if clock.Elapsed < timeout then
              do! Task.Delay 250
              return! attempt ()
            else
              return Error PortInUse
          | inner -> return Error(Failed inner.Message)
    }

  attempt ()

/// Disposes `watch` when the app stops, or at once when the app does not start.
let serve (name: string) (url: string) (routes: WebApplication -> unit) (watch: IDisposable) =
  task {
    let! result = start (TimeSpan.FromSeconds 30.0) url routes

    match result with
    | Ok app -> app.Lifetime.ApplicationStopping.Register(fun () -> watch.Dispose()) |> ignore
    | Error reason ->
      watch.Dispose()

      let message =
        match reason with
        | PortInUse -> "Another process uses the port."
        | StoppedBeforeStart -> "The app stopped before it started."
        | Failed message -> message

      eprintfn "%s: the server did not start on %s: %s" name url message

    return result
  }
