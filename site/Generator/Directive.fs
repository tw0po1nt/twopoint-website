/// A leaf directive alone on its line, for example `::youtube{id="qRuKTkDvAk4"}`.
[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module TwoPoint.Site.Generator.Directive

open Markdig.Parsers
open Markdig.Syntax
open System
open System.Net
open System.Text.RegularExpressions

type Directive =
  | Pdf of file: Uri
  | YouTube of id: string

let private names = "pdf, youtube"

let private shape = Regex("""^::([A-Za-z][\w-]*)\{(.*)\}$""")

let private attributeList = Regex("""^(?:\s*([A-Za-z][\w-]*)="([^"]*)")*\s*$""")

let private youTubeId = Regex("^[A-Za-z0-9_-]{11}$")

let private attributes name (text: string) =
  let found = attributeList.Match text

  if not found.Success then
    Error $"Give each attribute of '::{name}' as name=\"value\"."
  else
    let pairs =
      [ for index in 0 .. found.Groups[1].Captures.Count - 1 ->
          found.Groups[1].Captures[index].Value, found.Groups[2].Captures[index].Value ]

    match pairs |> List.countBy fst |> List.tryFind (fun (_, count) -> count > 1) with
    | Some(attribute, _) -> Error $"The directive '::{name}' has the attribute '{attribute}' more than one time."
    | None -> Ok(Map.ofList pairs)

let private single name attribute (values: Map<string, string>) =
  match values |> Map.keys |> Seq.tryFind (fun key -> key <> attribute) with
  | Some unknown -> Error $"The directive '::{name}' has the unknown attribute '{unknown}'. Use only '{attribute}'."
  | None ->
    match values.TryFind attribute with
    | Some value -> Ok value
    | None -> Error $"The directive '::{name}' has no '{attribute}' attribute."

let private pdf value =
  match Uri.TryCreate(value, UriKind.Absolute) with
  | true, uri when uri.Scheme = Uri.UriSchemeHttps && uri.AbsolutePath.EndsWith ".pdf" -> Ok(Pdf uri)
  | _ -> Error $"The 'file' of '::pdf' is '{value}'. Give the https URL of a .pdf file."

let private youTube (value: string) =
  if youTubeId.IsMatch value then
    Ok(YouTube value)
  else
    Error $"The 'id' of '::youtube' is '{value}'. Give a YouTube video id of 11 letters, digits, '-', or '_'."

let parse (line: string) : Result<Directive, string> =
  let found = shape.Match(line.Trim())

  if not found.Success then
    Error $"Write the directive as ::name{{attribute=\"value\"}}, alone on its line. Use one of these names: {names}."
  else
    let name = found.Groups[1].Value
    let values = attributes name found.Groups[2].Value

    match name with
    | "pdf" -> values |> Result.bind (single name "file") |> Result.bind pdf
    | "youtube" -> values |> Result.bind (single name "id") |> Result.bind youTube
    | _ -> Error $"The directive '::{name}' is unknown. Use one of these names: {names}."

let html (directive: Directive) =
  match directive with
  | Pdf file ->
    let href = WebUtility.HtmlEncode file.AbsoluteUri
    $"<figure><a href=\"{href}\" target=\"_blank\" rel=\"noopener noreferrer\" download>Download</a></figure>\n"
  | YouTube id ->
    $"<iframe src=\"https://www.youtube-nocookie.com/embed/{id}\" title=\"YouTube video player\" \
      allow=\"accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture\" \
      referrerpolicy=\"strict-origin-when-cross-origin\" allowfullscreen loading=\"lazy\"></iframe>\n"

type DirectiveBlock(parser: BlockParser, directive: Result<Directive, string>) =
  inherit LeafBlock(parser)
  member _.Directive = directive

type DirectiveParser() as this =
  inherit BlockParser()
  do this.OpeningCharacters <- [| ':' |]

  override _.TryOpen(processor: BlockProcessor) =
    let line = processor.Line.ToString()

    if processor.IsCodeIndent || not (line.StartsWith "::") then
      BlockState.None
    else
      let block = DirectiveBlock(this, parse line)
      block.Line <- processor.LineIndex
      block.Column <- processor.Column
      block.Span <- SourceSpan(processor.Start, processor.Line.End)
      processor.NewBlocks.Push block
      BlockState.BreakDiscard
