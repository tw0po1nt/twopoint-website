[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module TwoPoint.Site.Render.PostText

open System
open System.IO
open Markdig
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

let private loadRoot (yaml: string) =
  let stream = YamlStream()

  try
    stream.Load(new StringReader(yaml))
  with :? YamlException ->
    ()

  match stream.Documents |> Seq.tryHead |> Option.map _.RootNode with
  | Some(:? YamlMappingNode as mapping) -> mapping
  | _ -> YamlMappingNode()

let private scalar (name: string) (root: YamlMappingNode) =
  root.Children
  |> Seq.tryPick (fun (KeyValue(key, value)) ->
    match key, value with
    | (:? YamlScalarNode as key), (:? YamlScalarNode as value) when key.Value = name -> Some value.Value
    | _ -> None)
  |> Option.defaultValue ""

let split (text: string) : Fields =
  let yaml, rest = frontMatterAndRest text
  let root = loadRoot yaml

  { Title = scalar "title" root
    Excerpt = scalar "excerpt" root
    Body = rest |> withoutLine |> withoutLine }

/// Writes the fields into the front matter of `baseText`. An empty title or excerpt removes the field.
let compose (baseText: string) (fields: Fields) : string =
  let yaml, _ = frontMatterAndRest baseText
  let root = loadRoot yaml

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
  $"---\n{front}\n---\n\n{fields.Body}"
