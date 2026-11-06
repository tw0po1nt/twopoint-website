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
    | Some _ -> Error "The front matter must be a single YAML mapping."
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

/// Body line 1 is the blank line that `compose` writes after the front matter, so the editor line is one less.
let editorLine (bodyLine: int) = max 1 (bodyLine - 1)

/// The front matter fields that the Details sheet edits. A missing or empty field is `None`, or an empty list.
type Details =
  { Author: string option
    /// The title of the category, as the front matter gives it.
    Category: string option
    /// The titles of the tags, as the front matter gives them.
    Tags: string list
    /// The slug of the series.
    Series: string option
    Image: string option
    ShowOnHomepage: bool }

let private textOf (node: YamlNode) =
  match node with
  | :? YamlScalarNode as value when not (String.IsNullOrWhiteSpace value.Value) -> Some value.Value
  | _ -> None

let private fieldNode (name: string) (root: YamlMappingNode) =
  root.Children
  |> Seq.tryPick (fun (KeyValue(key, value)) ->
    match key with
    | :? YamlScalarNode as key when key.Value = name -> Some value
    | _ -> None)

let private detailsOf (root: YamlMappingNode) =
  let text name = fieldNode name root |> Option.bind textOf

  { Author = text "author"
    Category = text "category"
    Tags =
      match fieldNode "tags" root with
      | Some(:? YamlSequenceNode as tags) -> tags.Children |> Seq.choose textOf |> List.ofSeq
      | _ -> []
    Series = text "series"
    Image = text "image"
    ShowOnHomepage = text "showOnHomepage" <> Some "false" }

/// Text that the YAML parser cannot read gives the fields of a post with no details.
let details (text: string) : Details =
  let yaml, _ = frontMatterAndRest text

  match loadRoot yaml with
  | Ok root -> detailsOf root
  | Error _ ->
    { Author = None
      Category = None
      Tags = []
      Series = None
      Image = None
      ShowOnHomepage = true }

let private setText (root: YamlMappingNode) (name: string) (value: string option) =
  let key = YamlScalarNode name

  match value with
  | Some value when not (String.IsNullOrWhiteSpace value) -> root.Children[key] <- YamlScalarNode value
  | _ -> root.Children.Remove key |> ignore

let private setDetails (details: Details) (root: YamlMappingNode) =
  let current = detailsOf root
  let key name = YamlScalarNode name

  if details.Author <> current.Author then setText root "author" details.Author
  if details.Category <> current.Category then setText root "category" details.Category
  if details.Series <> current.Series then setText root "series" details.Series
  if details.Image <> current.Image then setText root "image" details.Image

  if details.Tags <> current.Tags then
    if details.Tags.IsEmpty then
      root.Children.Remove(key "tags") |> ignore
    else
      root.Children[key "tags"] <- YamlSequenceNode(details.Tags |> List.map (fun tag -> YamlScalarNode tag :> YamlNode))

  if details.ShowOnHomepage <> current.ShowOnHomepage then
    if details.ShowOnHomepage then
      root.Children.Remove(key "showOnHomepage") |> ignore
    else
      root.Children[key "showOnHomepage"] <- YamlScalarNode "false"

let private rewrite (baseText: string) (change: YamlMappingNode -> unit) (body: string) : Result<string, string> =
  let yaml, _ = frontMatterAndRest baseText

  loadRoot yaml
  |> Result.map (fun root ->
    change root

    // Markdig reads `---` and `---` with nothing between as a thematic break, so an empty mapping is `{}`.
    root.Style <- if root.Children.Count = 0 then MappingStyle.Flow else MappingStyle.Block

    use writer = new StringWriter()
    YamlStream(YamlDocument root).Save(writer, false)
    let front = writer.ToString().Replace("\r\n", "\n").Replace("\n...\n", "\n").TrimEnd '\n'
    $"---\n{front}\n---\n\n{body}")

let private setFields (fields: Fields) (root: YamlMappingNode) =
  setText root "title" (Some fields.Title)
  setText root "excerpt" (Some fields.Excerpt)

/// Writes the fields into the front matter of `baseText`. An empty title or excerpt removes the field.
/// Front matter that the YAML parser cannot read gives the reason, so a save never drops its other fields.
let compose (baseText: string) (fields: Fields) : Result<string, string> =
  rewrite baseText (setFields fields) fields.Body

/// Like `compose`, and also writes the details. A detail that is the same as the stored one leaves its field as it is.
let composeWithDetails (baseText: string) (fields: Fields) (details: Details) : Result<string, string> =
  rewrite baseText (fun root -> setFields fields root; setDetails details root) fields.Body

let private withDate (key: string) (baseText: string) (date: DateTimeOffset) : Result<string, string> =
  rewrite baseText (fun root -> setText root key (Some(Time.isoTime date))) (split baseText).Body

/// Front matter that the YAML parser cannot read gives the reason, as `compose` does.
let withPublishDate (baseText: string) (publishDate: DateTimeOffset) : Result<string, string> =
  withDate "publishDate" baseText publishDate

let withUpdateDate (baseText: string) (updateDate: DateTimeOffset) : Result<string, string> =
  withDate "updateDate" baseText updateDate

let withoutPublishDate (baseText: string) : Result<string, string> =
  rewrite baseText (fun root -> setText root "publishDate" None) (split baseText).Body

/// The YAML text of a series record. `FrontMatter.parseSeries` reads it back to the same series.
let seriesRecord (series: Series) : string =
  let root = YamlMappingNode()
  root.Children[YamlScalarNode "title"] <- YamlScalarNode series.Title
  root.Children[YamlScalarNode "excerpt"] <- YamlScalarNode series.Excerpt

  match series.Image with
  | Some image -> root.Children[YamlScalarNode "image"] <- YamlScalarNode(string image)
  | None -> ()

  use writer = new StringWriter()
  YamlStream(YamlDocument root).Save(writer, false)
  writer.ToString().Replace("\r\n", "\n").Replace("\n...\n", "\n").TrimEnd '\n' + "\n"
