namespace TwoPoint.Site.Generator

open TwoPoint.Site.Widgets

open FsToolkit.ErrorHandling
open System
open System.Text
open System.Text.Json
open System.Text.RegularExpressions

/// `File` and each import are paths in `frontend/Widgets/dist/`, with `/`.
type WidgetEntry = { File: string; Imports: string list }

/// Each file is a path in `frontend/Widgets/dist/`, with `/`.
type WidgetManifest =
  { Entries: Map<string, WidgetEntry>
    Head: string
    /// Only the dev server uses this script.
    DevReload: string }

type Widgets =
  { Manifest: WidgetManifest
    HeadScript: string
    /// Each file at its site path, for example `_/widgets/chunk-1a2b3c4d.js`.
    Files: Map<string, byte array>
    /// The dev reload script and each source map, at its site path. Only the dev server serves them.
    DevFiles: Map<string, byte array> }

/// Each value is a URL path from the root, for example `/_/widgets/subscribe-1a2b3c4d.js`.
type WidgetScripts =
  { Preloads: string list
    Entries: string list }

[<RequireQualifiedAccess>]
module WidgetManifest =

  let folder = "frontend/Widgets/dist"

  let file = $"{folder}/manifest.json"

  let private property (element: JsonElement) (key: string) (kind: JsonValueKind) =
    match element.TryGetProperty key with
    | true, value when value.ValueKind = kind -> Some value
    | _ -> None

  let private text (element: JsonElement) (key: string) (where: string) =
    match property element key JsonValueKind.String with
    | Some value -> Ok(value.GetString())
    | None -> Error [ $"{file} has no '{key}' text {where}." ]

  let private entry (name: string) (element: JsonElement) =
    let where = $"in the entry '{name}'"

    let imports =
      match property element "imports" JsonValueKind.Array with
      | Some imports when imports.EnumerateArray() |> Seq.forall (fun item -> item.ValueKind = JsonValueKind.String) ->
        Ok [ for item in imports.EnumerateArray() -> item.GetString() ]
      | _ -> Error [ $"{file} has no 'imports' list of text {where}." ]

    validation {
      let! entryFile = text element "file" where
      and! imports = imports
      return name, { File = entryFile; Imports = imports }
    }

  let private entries (root: JsonElement) =
    match property root "entries" JsonValueKind.Object with
    | Some entries ->
      entries.EnumerateObject()
      |> List.ofSeq
      |> List.traverseResultA (fun item -> entry item.Name item.Value)
      |> Result.mapError List.concat
      |> Result.map Map.ofList
    | None -> Error [ $"{file} has no 'entries' object." ]

  let parse (json: string) : Result<WidgetManifest, string list> =
    try
      use document = JsonDocument.Parse json
      let root = document.RootElement

      validation {
        let! entries = entries root
        and! head = text root "head" "at the top level"
        and! devReload = text root "devReload" "at the top level"

        return
          { Entries = entries
            Head = head
            DevReload = devReload }
      }
    with :? JsonException as error ->
      Error [ $"{file} is not valid JSON: {error.Message}" ]

[<RequireQualifiedAccess>]
module Widgets =

  let sitePath (name: string) = $"_/widgets/{name}"

  /// `files` are the files of `frontend/Widgets/dist/`, by path in that folder, with `/`.
  let create (files: Map<string, byte array>) : Result<Widgets, string list> =
    result {
      let! manifestBytes =
        files.TryFind "manifest.json"
        |> Result.requireSome [ $"{WidgetManifest.file} does not exist. Run `npm ci` and `npm run build` in frontend/Widgets." ]

      let! manifest = WidgetManifest.parse (Encoding.UTF8.GetString manifestBytes)

      do!
        match
          [ for name in Contract.names do
              if not (manifest.Entries.ContainsKey name) then
                $"{WidgetManifest.file} has no entry for the widget '{name}'."
            for name in manifest.Entries.Keys do
              if not (List.contains name Contract.names) then
                $"{WidgetManifest.file} has the entry '{name}', and the widget contract has no such widget." ]
        with
        | [] -> Ok()
        | errors -> Error errors

      let named =
        [ manifest.Head
          manifest.DevReload
          for entry in manifest.Entries.Values do
            entry.File
            yield! entry.Imports ]

      do!
        match named |> List.distinct |> List.filter (fun name -> not (files.ContainsKey name)) with
        | [] -> Ok()
        | missing -> Error [ for name in missing -> $"{WidgetManifest.file} names {name}, and {WidgetManifest.folder}/ has no such file." ]

      let headScript = Encoding.UTF8.GetString files[manifest.Head]

      do!
        headScript.Contains("</script", StringComparison.OrdinalIgnoreCase)
        |> Result.requireFalse [ $"{WidgetManifest.folder}/{manifest.Head} contains '</script', so a page cannot put it inline." ]

      let isDevFile (name: string) =
        name = manifest.DevReload || name.EndsWith ".map"

      let atSitePaths (files: Map<string, byte array>) =
        files |> Seq.map (fun (KeyValue(name, bytes)) -> sitePath name, bytes) |> Map.ofSeq

      let siteFiles, devFiles =
        files
        |> Map.filter (fun name _ -> name <> "manifest.json" && name <> manifest.Head)
        |> Map.partition (fun name _ -> not (isDevFile name))

      return
        { Manifest = manifest
          HeadScript = headScript
          Files = atSitePaths siteFiles
          DevFiles = atSitePaths devFiles }
    }

  let private mount = Regex("\\sdata-widget=\"([^\"]+)\"")

  let scripts (widgets: Widgets) (html: string) : WidgetScripts =
    let entries =
      [ for found in mount.Matches html -> found.Groups[1].Value ]
      |> List.distinct
      |> List.choose widgets.Manifest.Entries.TryFind

    let urlPath name = $"/{sitePath name}"

    { Preloads = entries |> List.collect _.Imports |> List.distinct |> List.map urlPath
      Entries = entries |> List.map (_.File >> urlPath) }
