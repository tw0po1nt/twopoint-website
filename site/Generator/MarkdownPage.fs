namespace TwoPoint.Site.Generator

open FsToolkit.ErrorHandling
open Markdig
open Markdig.Extensions.Yaml
open System
open System.IO
open YamlDotNet.Core
open YamlDotNet.RepresentationModel

/// A page of the site that is not a post, from a Markdown file with a title and a description.
type MarkdownPage =
  { /// The path from the repo root.
    File: string
    Title: string
    Description: string
    Body: Body }

[<RequireQualifiedAccess>]
module MarkdownPage =

  let private fieldNames = [ "title"; "description" ]

  /// `startLine` is the file line of the opening `---`.
  let private frontMatter (file: string) (startLine: int) (yaml: string) =
    let errorAt (yamlLine: int64) message = PostError.at file (startLine + int yamlLine) message

    let ofMapping (mapping: YamlMappingNode) =
      let fields =
        mapping.Children
        |> Seq.choose (fun (KeyValue(key, value)) ->
          match key with
          | :? YamlScalarNode as scalar -> Some(scalar.Value, (key, value))
          | _ -> None)
        |> Map.ofSeq

      let unknown =
        fields
        |> Map.toList
        |> List.filter (fun (field, _) -> not (List.contains field fieldNames))
        |> List.map (fun (field, (key, _)) -> errorAt key.Start.Line $"Unknown field '{field}'.")

      let text name =
        match fields.TryFind name with
        | Some(_, (:? YamlScalarNode as scalar)) when not (String.IsNullOrWhiteSpace scalar.Value) -> Ok scalar.Value
        | Some(_, node) -> Error [ errorAt node.Start.Line $"Give '{name}' as text." ]
        | None -> Error [ errorAt 0L $"The front matter has no '{name}' field." ]

      validation {
        let! title = text "title"
        and! description = text "description"
        and! () = if unknown.IsEmpty then Ok() else Error unknown
        return title, description
      }
      |> Result.mapError (List.sortBy _.Line)

    try
      let stream = YamlStream()
      stream.Load(new StringReader(yaml))

      match List.ofSeq stream.Documents with
      | [ document ] ->
        match document.RootNode with
        | :? YamlMappingNode as mapping -> ofMapping mapping
        | node -> Error [ errorAt node.Start.Line "Give the front matter as a YAML mapping." ]
      | _ -> Error [ errorAt 0L "The front matter is not one YAML mapping." ]
    with :? YamlException as error ->
      Error [ errorAt error.Start.Line error.Message ]

  /// `file` is the path from the repo root.
  let parse (fetch: IImageFetch) (file: string) (text: string) : Result<MarkdownPage, PostError list> =
    let document = Markdown.Parse(text, Body.pipeline)

    match Seq.tryHead document with
    | Some(:? YamlFrontMatterBlock as block) when block.Line = 0 ->
      result {
        let! title, description = frontMatter file (block.Line + 1) (block.Lines.ToString())
        let! body = Body.ofDocument fetch file document

        return
          { File = file
            Title = title
            Description = description
            Body = body }
      }
    | _ -> Error [ PostError.at file 1 "The page has no front matter. Start the file with a '---' line." ]
