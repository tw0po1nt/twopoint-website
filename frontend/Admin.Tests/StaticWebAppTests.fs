module TwoPoint.Admin.Tests.StaticWebAppTests

open TwoPoint.Admin.Dev

open Expecto
open System
open System.IO
open System.Text

let private config =
  match StaticWebApp.parse (File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "..", "Admin", "staticwebapp.config.json"))) with
  | Ok config -> config
  | Error message -> failwith message

let private build =
  Map
    [ "/index.html", "<p>admin</p>"
      "/redirect/index.html", "<p>bridge</p>"
      "/bundle/admin-ABC.js", "admin();"
      "/bundle/style-DEF.css", "p {}" ]

let private find (files: Map<string, string>) (path: string) =
  match Map.tryFind path files with
  | Some text -> Found(Encoding.UTF8.GetBytes text)
  | None when files |> Map.exists (fun file _ -> file.StartsWith(path.TrimEnd '/' + "/")) -> Folder
  | None -> Missing

let private get (path: string) = StaticWebApp.reply config (find build) path

let private body (reply: Reply) = Encoding.UTF8.GetString reply.Body

[<Tests>]
let tests =
  testList "StaticWebApp" [
    test "a deep path with no file gets index.html" {
      for path in [ "/"; "/posts/x"; "/comments"; "/posts/x/" ] do
        let reply = get path
        Expect.equal (reply.Status, body reply) (200, "<p>admin</p>") path
    }

    test "each response has the X-Robots-Tag header" {
      for path in [ "/"; "/posts/x"; "/redirect/"; "/bundle/admin-ABC.js"; "/bundle/nothing.js" ] do
        Expect.equal (Map.tryFind "X-Robots-Tag" (get path).Headers) (Some "noindex, nofollow") path
    }

    test "the bridge page has no cache and no COOP header" {
      let reply = get "/redirect/"
      Expect.equal (reply.Status, body reply) (200, "<p>bridge</p>") "The bridge page"
      Expect.equal (Map.tryFind "Cache-Control" reply.Headers) (Some "no-store") "No cache"
      Expect.isFalse (reply.Headers.ContainsKey "Cross-Origin-Opener-Policy") "No COOP header"
    }

    test "the bridge path with no slash goes to the bridge page" {
      let reply = get "/redirect"
      Expect.equal (reply.Status, Map.tryFind "Location" reply.Headers) (301, Some "/redirect/") "A redirect"
    }

    test "a bundle file or a bridge path with no file gets a 404" {
      for path in [ "/bundle/nothing.js"; "/redirect/nothing" ] do
        Expect.equal (get path).Status 404 path
    }

    test "a bundle file gets its content type" {
      let script = get "/bundle/admin-ABC.js"
      Expect.equal script.Status 200 "The script"
      Expect.stringStarts script.Headers["Content-Type"] "text/javascript" "A script"
      Expect.stringStarts (get "/bundle/style-DEF.css").Headers["Content-Type"] "text/css" "A style sheet"
    }

    test "a path before the first build gets a 503" {
      for path in [ "/"; "/posts/x" ] do
        Expect.equal (StaticWebApp.reply config (find Map.empty) path).Status 503 path
    }

    test "the disk lookup finds no file outside the build folder" {
      let root = Path.Combine(Path.GetTempPath(), $"admin-{Guid.NewGuid():N}")
      let build = Path.Combine(root, "dist")
      Directory.CreateDirectory build |> ignore
      File.WriteAllText(Path.Combine(root, "secret.txt"), "secret")

      try
        Expect.equal (Server.find build "/../secret.txt") Missing "The file next to the build folder"
        Expect.equal (Server.find build "/") Folder "The build folder"
      finally
        Directory.Delete(root, true)
    }
  ]
