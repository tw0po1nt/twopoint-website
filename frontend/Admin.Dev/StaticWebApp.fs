namespace TwoPoint.Admin.Dev

open FsToolkit.ErrorHandling
open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

type RouteHeaders =
  { Route: string
    Headers: Map<string, string> }

type NavigationFallback =
  { Rewrite: string
    Exclude: string list }

/// The part of `staticwebapp.config.json` that the admin uses.
type StaticWebAppConfig =
  { GlobalHeaders: Map<string, string>
    Routes: RouteHeaders list
    MimeTypes: Map<string, string>
    NavigationFallback: NavigationFallback }

type Entry =
  | Found of bytes: byte array
  | Folder
  | Missing

type Reply =
  { Status: int
    Headers: Map<string, string>
    Body: byte array }

[<RequireQualifiedAccess>]
module StaticWebApp =

  let private file = "staticwebapp.config.json"

  let private property (element: JsonElement) (key: string) (kind: JsonValueKind) =
    match element.TryGetProperty key with
    | true, value when value.ValueKind = kind -> Ok value
    | _ -> Error $"{file} has no '{key}' {kind}."

  let private headers (element: JsonElement) =
    element.EnumerateObject() |> Seq.map (fun item -> item.Name, item.Value.GetString()) |> Map.ofSeq

  let private route (element: JsonElement) =
    result {
      let! route = property element "route" JsonValueKind.String
      let! values = property element "headers" JsonValueKind.Object
      return { Route = route.GetString(); Headers = headers values }
    }

  let parse (json: string) : Result<StaticWebAppConfig, string> =
    try
      use document = JsonDocument.Parse json
      let root = document.RootElement

      result {
        let! globalHeaders = property root "globalHeaders" JsonValueKind.Object
        let! routes = property root "routes" JsonValueKind.Array
        let! routes = routes.EnumerateArray() |> List.ofSeq |> List.traverseResultM route
        let mimeTypes =
          match root.TryGetProperty "mimeTypes" with
          | true, value when value.ValueKind = JsonValueKind.Object -> headers value
          | _ -> Map.empty

        let! fallback = property root "navigationFallback" JsonValueKind.Object
        let! rewrite = property fallback "rewrite" JsonValueKind.String
        let! exclude = property fallback "exclude" JsonValueKind.Array

        return
          { GlobalHeaders = headers globalHeaders
            Routes = routes
            MimeTypes = mimeTypes
            NavigationFallback =
              { Rewrite = rewrite.GetString()
                Exclude = [ for item in exclude.EnumerateArray() -> item.GetString() ] } }
      }
    with :? JsonException as error ->
      Error $"{file} is not valid JSON: {error.Message}"

  /// A `*` in an Azure Static Web Apps route pattern matches any text.
  let matches (pattern: string) (path: string) =
    let expression = pattern.Split '*' |> Array.map Regex.Escape |> String.concat ".*"
    Regex.IsMatch(path, $"^{expression}$")

  let private headersFor (config: StaticWebAppConfig) (path: string) =
    config.Routes
    |> List.tryFind (fun route -> matches route.Route path)
    |> Option.map _.Headers
    |> Option.defaultValue Map.empty
    |> Map.fold (fun all name value -> Map.add name value all) config.GlobalHeaders

  let private contentTypes =
    Map
      [ ".css", "text/css; charset=utf-8"
        ".html", "text/html; charset=utf-8"
        ".js", "text/javascript; charset=utf-8"
        ".json", "application/json"
        ".map", "application/json"
        ".png", "image/png" ]

  /// The `mimeTypes` of the config win, as they do in Azure Static Web Apps.
  let private contentType (config: StaticWebAppConfig) (file: string) =
    let extension = Path.GetExtension file

    config.MimeTypes
    |> Map.tryFind extension
    |> Option.orElse (Map.tryFind extension contentTypes)
    |> Option.defaultValue "application/octet-stream"

  /// `find` gives the entry of the build folder at a site path, such as `/redirect/index.html`.
  let reply (config: StaticWebAppConfig) (find: string -> Entry) (path: string) : Reply =
    let path = if String.IsNullOrEmpty path then "/" else path
    let headers = headersFor config path

    let send (file: string) (bytes: byte array) =
      { Status = 200
        Headers = headers |> Map.add "Content-Type" (contentType config file)
        Body = bytes }

    let text (status: int) (body: string) =
      { Status = status
        Headers = headers |> Map.add "Content-Type" "text/plain; charset=utf-8"
        Body = Encoding.UTF8.GetBytes body }

    let index = $"{path}index.html"
    let fallback = config.NavigationFallback

    if not (path.StartsWith '/') || path.Split('/') |> Array.contains ".." then
      { Status = 400; Headers = config.GlobalHeaders; Body = [||] }
    else
      match find path, find index with
      | Folder, _ when not (path.EndsWith '/') ->
        { Status = 301
          Headers = headers |> Map.add "Location" $"{path}/"
          Body = [||] }
      | Folder, Found bytes -> send index bytes
      | Found bytes, _ -> send path bytes
      | _ when fallback.Exclude |> List.exists (fun pattern -> matches pattern path) -> text 404 "Not found"
      | _ ->
        match find fallback.Rewrite with
        | Found bytes -> send fallback.Rewrite bytes
        | _ -> text 503 "The first build is not ready"
