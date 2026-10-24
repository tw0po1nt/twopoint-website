[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module TwoPoint.Site.Render.PostText

open Markdig
open System
open System.IO
open YamlDotNet.Core
open YamlDotNet.Core.Events
open YamlDotNet.RepresentationModel

/// The parts of a post that the editor shows. The other front matter fields stay in the text.
type Fields =
  { Title: string
    Excerpt: string
    Body: string }

let private frontMatterAndRest (text: string) =
  match Body.frontMatter (Markdown.Parse(text, Body.pipeline)) with
  | Some block -> block.Lines.ToString(), text.Substring(min text.Length (block.Span.End + 1))
  | None -> "", text

let private withoutLine (text: string) =
  if text.StartsWith "\r\n" then text.Substring 2
  elif text.StartsWith "\n" then text.Substring 1
  else text

/// An empty front matter gives an empty mapping. A front matter that is not one YAML mapping gives the reason.
let private loadRoot (yaml: string) : Result<YamlMappingNode, string> =
  let stream = YamlStream()

  try
    stream.Load(new StringReader(yaml))

    match stream.Documents |> Seq.tryHead |> Option.map _.RootNode with
    | None -> Ok(YamlMappingNode())
    | Some(:? YamlMappingNode as mapping) -> Ok mapping
    | Some _ -> Error "The front matter is not a YAML mapping."
  with
  // An unterminated flow sequence makes YamlDotNet throw InvalidOperationException.
  | :? YamlException
  | :? InvalidOperationException as error -> Error error.Message

let private scalar (name: string) (root: YamlMappingNode) =
  root.Children
  |> Seq.tryPick (fun (KeyValue(key, value)) ->
    match key, value with
    | (:? YamlScalarNode as key), (:? YamlScalarNode as value) when key.Value = name -> Some value.Value
    | _ -> None)
  |> Option.defaultValue ""

/// Text that the YAML parser cannot read gives empty fields.
let split (text: string) : Fields =
  let yaml, rest = frontMatterAndRest text

  let title, excerpt =
    match loadRoot yaml with
    | Ok root -> scalar "title" root, scalar "excerpt" root
    | Error _ -> "", ""

  { Title = title
    Excerpt = excerpt
    Body = rest |> withoutLine |> withoutLine }

/// Writes the fields into the front matter of `baseText`. An empty title or excerpt removes the field.
/// Front matter that the YAML parser cannot read gives the reason, so a save never drops its other fields.
let compose (baseText: string) (fields: Fields) : Result<string, string> =
  let yaml, _ = frontMatterAndRest baseText

  loadRoot yaml
  |> Result.map (fun root ->
    for name, value in [ "title", fields.Title; "excerpt", fields.Excerpt ] do
      let key = YamlScalarNode name

      if String.IsNullOrWhiteSpace value then
        root.Children.Remove key |> ignore
      else
        root.Children[key] <- YamlScalarNode value

    // Markdig reads `---` and `---` with nothing between as a thematic break, so an empty mapping is `{}`.
    root.Style <- if root.Children.Count = 0 then MappingStyle.Flow else MappingStyle.Block

    use writer = new StringWriter()
    YamlStream(YamlDocument root).Save(writer, false)
    let front = writer.ToString().Replace("\r\n", "\n").Replace("\n...\n", "\n").TrimEnd '\n'
    $"---\n{front}\n---\n\n{fields.Body}")
