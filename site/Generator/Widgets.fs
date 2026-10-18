namespace TwoPoint.Site.Generator

open FsToolkit.ErrorHandling
open System
open System.Text
open System.Text.Json

/// `File` and each import are paths in `site/Widgets/dist/`, with `/`.
type WidgetEntry = { File: string; Imports: string list }

/// Each file is a path in `site/Widgets/dist/`, with `/`.
type WidgetManifest =
  { Entries: Map<string, WidgetEntry>
    Head: string
    /// Only the dev server uses this script.
    DevReload: string }

type Widgets =
  { Manifest: WidgetManifest
    /// The text of the inline `<script>` in each page head.
    HeadScript: string
    /// Each file at its site path, for example `_/widgets/chunk-1a2b3c4d.js`.
    Files: Map<string, byte array> }

[<RequireQualifiedAccess>]
module WidgetManifest =

  let file = "site/Widgets/dist/manifest.json"

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

  let private folder = "site/Widgets/dist"

  let sitePath (name: string) = $"_/widgets/{name}"

  /// `files` are the files of `site/Widgets/dist/`, by path in that folder, with `/`.
  let create (files: Map<string, byte array>) : Result<Widgets, string list> =
    result {
      let! manifestBytes =
        files.TryFind "manifest.json"
        |> Result.requireSome [ $"{WidgetManifest.file} does not exist. Run `npm ci` and `npm run build` in site/Widgets." ]

      let! manifest = WidgetManifest.parse (Encoding.UTF8.GetString manifestBytes)

      let named =
        [ manifest.Head
          manifest.DevReload
          for entry in manifest.Entries.Values do
            entry.File
            yield! entry.Imports ]

      do!
        match named |> List.distinct |> List.filter (fun name -> not (files.ContainsKey name)) with
        | [] -> Ok()
        | missing -> Error [ for name in missing -> $"{WidgetManifest.file} names {name}, and {folder}/ has no such file." ]

      let headScript = Encoding.UTF8.GetString files[manifest.Head]

      do!
        headScript.Contains("</script", StringComparison.OrdinalIgnoreCase)
        |> Result.requireFalse [ $"{folder}/{manifest.Head} contains '</script', so a page cannot put it inline." ]

      let excluded = set [ "manifest.json"; manifest.Head; manifest.DevReload ]

      return
        { Manifest = manifest
          HeadScript = headScript
          Files =
            files
            |> Map.filter (fun name _ -> not (excluded.Contains name))
            |> Seq.map (fun (KeyValue(name, bytes)) -> sitePath name, bytes)
            |> Map.ofSeq }
    }
