module TwoPoint.Site.Dev.Tests.ListenTests

open TwoPoint.DevHost

open Expecto
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open System
open System.Net
open System.Net.Http
open System.Net.Sockets

let private hello (app: WebApplication) =
  app.MapGet("/", RequestDelegate(fun ctx -> ctx.Response.WriteAsync "ok")) |> ignore

let private occupy () =
  let owner = new TcpListener(IPAddress.Loopback, 0)
  owner.Start()
  owner, (owner.LocalEndpoint :?> IPEndPoint).Port

let private freePort () =
  let owner, port = occupy ()
  owner.Stop()
  port

let private body (port: int) =
  use client = new HttpClient()
  client.GetStringAsync($"http://localhost:{port}/").Result

let private served (port: int) (result: Result<WebApplication, string>) =
  match result with
  | Ok app ->
    try
      body port
    finally
      app.StopAsync().Wait()
  | Error message -> failtest message

[<Tests>]
let tests =
  testList "Listen" [
    test "an app on a free port serves its routes" {
      let port = freePort ()
      let server = Listen.start (TimeSpan.FromSeconds 5.0) $"http://localhost:{port}" hello
      Expect.equal (served port server.Result) "ok" "The app serves the route"
    }

    test "the start does not wait for a port in use, and the app starts when the port is released" {
      let owner, port = occupy ()
      let server = Listen.start (TimeSpan.FromSeconds 10.0) $"http://localhost:{port}" hello
      Expect.isFalse server.IsCompleted "The start returns while another process uses the port"
      owner.Stop()
      Expect.equal (served port server.Result) "ok" "The app serves the route after the release"
    }

    test "a port in use after the timeout gives the bind error" {
      let owner, port = occupy ()

      try
        match (Listen.start (TimeSpan.FromMilliseconds 500.0) $"http://localhost:{port}" hello).Result with
        | Ok app ->
          app.StopAsync().Wait()
          failtest "The app started on a port in use"
        | Error message -> Expect.stringContains message "address already in use" "The error names the bind failure"
      finally
        owner.Stop()
    }
  ]
