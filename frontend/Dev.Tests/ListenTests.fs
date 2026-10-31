module TwoPoint.Site.Dev.Tests.ListenTests

open TwoPoint.DevHost

open Expecto
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open System
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Threading.Tasks

let private hello (app: WebApplication) =
  app.MapGet("/", RequestDelegate(fun ctx -> ctx.Response.WriteAsync "ok")) |> ignore

let private occupy () =
  let owner = new TcpListener(IPAddress.Loopback, 0)
  owner.Start()
  owner, (owner.LocalEndpoint :?> IPEndPoint).Port

let private stopping (app: WebApplication) =
  { new IAsyncDisposable with
      member _.DisposeAsync() = ValueTask(app.StopAsync()) }

let private served (result: Result<WebApplication, Listen.NotStarted>) : Task<string> =
  task {
    match result with
    | Ok app ->
      use _ = stopping app
      use client = new HttpClient()
      return! client.GetStringAsync(Seq.head app.Urls + "/")
    | Error reason -> return failtestf "The app did not start: %A" reason
  }

[<Tests>]
let tests =
  testList "Listen" [
    testTask "an app on a free port serves its routes" {
      let! result = Listen.start (TimeSpan.FromSeconds 5.0) "http://127.0.0.1:0" hello
      let! body = served result
      Expect.equal body "ok" "The app serves the route"
    }

    testTask "the start does not wait for a port in use, and the app starts when the port is released" {
      let owner, port = occupy ()
      let server = Listen.start (TimeSpan.FromSeconds 10.0) $"http://localhost:{port}" hello
      Expect.isFalse server.IsCompleted "The start returns while another process uses the port"
      owner.Stop()
      let! result = server
      let! body = served result
      Expect.equal body "ok" "The app serves the route after the release"
    }

    testTask "a port in use after the timeout gives PortInUse" {
      let owner, port = occupy ()

      try
        match! Listen.start (TimeSpan.FromMilliseconds 500.0) $"http://localhost:{port}" hello with
        | Ok(app: WebApplication) ->
          do! app.StopAsync()
          failtest "The app started on a port in use"
        | Error reason -> Expect.equal reason Listen.PortInUse "The reason is the port in use"
      finally
        owner.Stop()
    }

    testTask "a served app disposes its watch when it stops" {
      let disposals = ref 0
      let watch = { new IDisposable with member _.Dispose() = disposals.Value <- disposals.Value + 1 }

      match! Listen.serve "test" "http://127.0.0.1:0" hello watch with
      | Ok(app: WebApplication) ->
        Expect.equal disposals.Value 0 "The watch runs while the app runs"
        do! app.StopAsync()
        Expect.equal disposals.Value 1 "The stop disposes the watch"
      | Error reason -> failtestf "The app did not start: %A" reason
    }
  ]
