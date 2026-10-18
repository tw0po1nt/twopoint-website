[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module TwoPoint.Site.Generator.Body

open Markdig
open Markdig.Extensions.SmartyPants
open Markdig.Renderers
open Markdig.Renderers.Html
open Markdig.Syntax
open Markdig.Syntax.Inlines
open System
open System.IO
open System.Text.RegularExpressions

let pipeline =
  MarkdownPipelineBuilder()
    .UseYamlFrontMatter()
    .UsePreciseSourceLocation()
    .UseSmartyPants()
    .UsePipeTables()
    .UseEmphasisExtras(Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough)
    .UseAutoLinks()
    .Build()

let private descendants<'a when 'a :> MarkdownObject> (document: MarkdownDocument) =
  MarkdownObjectExtensions.Descendants<'a>(document :> MarkdownObject) |> List.ofSeq

let rec private inlineText (node: Inline) =
  match node with
  | :? LiteralInline as literal -> literal.Content.ToString()
  | :? CodeInline as code -> code.Content
  | :? LineBreakInline as lineBreak -> if lineBreak.IsHard then "" else "\n"
  | :? HtmlEntityInline as entity -> entity.Transcoded.ToString()
  | :? SmartyPant as smartyPant -> string smartyPant.OpeningCharacter
  | :? AutolinkInline as autolink -> autolink.Url
  | :? ContainerInline as container -> container |> Seq.map inlineText |> String.concat ""
  | _ -> ""

let private leafText (leaf: LeafBlock) =
  match leaf with
  | :? CodeBlock as code -> code.Lines.ToString()
  | :? HtmlBlock -> ""
  | _ when isNull leaf.Inline -> ""
  | _ -> inlineText leaf.Inline

/// The text that the `reading-time` count of the Astro build sees: each node, code included.
let rec private blockText (block: Block) =
  match block with
  | :? Extensions.Yaml.YamlFrontMatterBlock -> ""
  | :? ContainerBlock as container -> container |> Seq.map blockText |> String.concat ""
  | :? LeafBlock as leaf -> leafText leaf
  | _ -> ""

let private readingTime (text: string) =
  let words = text.Split([| ' '; '\n'; '\r'; '\t' |], StringSplitOptions.RemoveEmptyEntries).Length
  int (ceil (float words / 200.0))

let private errorAt file (line: int) message = PostError.at file (line + 1) message

let private attributePattern = """\s+([^\s"'>/=]+)(?:\s*=\s*(?:"[^"]*"|'[^']*'|[^\s"'=<>`]+))?"""

let private attribute = Regex(attributePattern)

let private startTag = Regex("""<([A-Za-z][\w:-]*)((?:""" + attributePattern + """)*)\s*/?>""")

let private styleAttributes = set [ "class"; "style" ]

let private rawHtmlErrors file (document: MarkdownDocument) =
  let errors line (html: string) =
    let lineAt index = line + html.AsSpan(0, index).Count '\n'

    [ for tag in startTag.Matches html do
        if tag.Groups[1].Value.Equals("a", StringComparison.OrdinalIgnoreCase) then
          errorAt file (lineAt tag.Index) "Write the link as a Markdown link, for example [the docs](https://example.com/)."

        let attributes = tag.Groups[2]

        for found in attribute.Matches attributes.Value do
          let name = found.Groups[1]

          if styleAttributes.Contains(name.Value.ToLowerInvariant()) then
            errorAt
              file
              (lineAt (attributes.Index + name.Index))
              $"Delete the '{name.Value}' attribute. A post has no class or style in raw HTML." ]

  let blocks =
    descendants<HtmlBlock> document
    |> List.collect (fun block -> errors block.Line (block.Lines.ToString()))

  let inlines =
    descendants<HtmlInline> document |> List.collect (fun html -> errors html.Line html.Tag)

  blocks @ inlines

let private ownHosts = set [ "twopoint.dev"; "www.twopoint.dev" ]

let private externalSchemes = set [ Uri.UriSchemeHttp; Uri.UriSchemeHttps ]

let private openInNewTab (node: MarkdownObject) =
  let attributes = node.GetAttributes()
  attributes.AddPropertyIfNotExist("target", "_blank")
  attributes.AddPropertyIfNotExist("rel", "noopener noreferrer")

let private splitAnchor (url: string) =
  match url.IndexOf '#' with
  | -1 -> url, None
  | index -> url.Substring(0, index), Some(url.Substring(index + 1))

type private LinkKind =
  | Internal of LinkTarget
  | External
  | Mail
  | Invalid of message: string

let private kind (url: string) =
  if String.IsNullOrEmpty url then
    Invalid "The link has no URL."
  elif url.StartsWith '#' then
    Internal(SamePage(url.Substring 1))
  elif url.StartsWith '/' && not (url.StartsWith "//") then
    Internal(Page(splitAnchor url))
  else
    match Uri.TryCreate(url, UriKind.Absolute) with
    | true, uri when externalSchemes.Contains uri.Scheme && ownHosts.Contains uri.Host ->
      Invalid $"Give the link to {url} as a root-relative link, for example {uri.PathAndQuery}."
    | true, uri when externalSchemes.Contains uri.Scheme -> External
    | true, uri when uri.Scheme = Uri.UriSchemeMailto -> Mail
    | _ -> Invalid $"Give the link to '{url}' as a root-relative link that starts with /, or as an http or https URL."

let private links (document: MarkdownDocument) =
  let links =
    descendants<LinkInline> document
    |> List.filter (fun link -> not link.IsImage)
    |> List.map (fun link -> link :> Inline, kind link.Url)

  let autolinks =
    descendants<AutolinkInline> document |> List.map (fun link -> link :> Inline, kind link.Url)

  links @ autolinks

let private lines (block: CodeBlock) =
  [ for index in 0 .. block.Lines.Count - 1 -> block.Lines.Lines[index].Slice.ToString() ]

let private tryFenceLanguage (block: CodeBlock) =
  match block with
  | :? FencedCodeBlock as fenced -> Highlight.tryLanguage fenced.Info |> Option.map (fun language -> fenced.Info, language)
  | _ -> None

type private CodeRenderer() =
  inherit HtmlObjectRenderer<CodeBlock>()

  override _.Write(renderer: HtmlRenderer, block: CodeBlock) =
    renderer.EnsureLine() |> ignore

    match tryFenceLanguage block with
    | Some(name, language) ->
      renderer
        .Write($"<pre data-lang=\"{name}\" tabindex=\"0\"><code>")
        .Write(Highlight.html language (lines block))
      |> ignore
    | None -> renderer.Write("<pre tabindex=\"0\"><code>").Write(Highlight.plain (lines block)) |> ignore

    renderer.WriteLine "</code></pre>" |> ignore

let private render (document: MarkdownDocument) =
  use writer = new StringWriter()
  let renderer = HtmlRenderer(writer)
  pipeline.Setup renderer
  renderer.ObjectRenderers.ReplaceOrAdd<CodeBlockRenderer>(CodeRenderer()) |> ignore
  renderer.Render document |> ignore
  writer.ToString()

/// Changes the document in place.
let ofDocument (file: string) (document: MarkdownDocument) : Result<Body, PostError list> =
  let headings = descendants<HeadingBlock> document
  let texts = headings |> List.map (fun heading -> inlineText heading.Inline)
  let ids = Slug.headingIds texts

  for heading, id in List.zip headings ids do
    heading.GetAttributes().Id <- id

  for image in descendants<LinkInline> document |> List.filter _.IsImage do
    image.GetAttributes().AddPropertyIfNotExist("loading", "lazy")

  let links = links document

  for node, kind in links do
    if kind = External then
      openInNewTab node

  let linkErrors =
    links
    |> List.choose (function
      | node, Invalid message -> Some(errorAt file node.Line message)
      | _ -> None)

  let internalLinks =
    links
    |> List.choose (function
      | node, Internal target -> Some { Target = target; Line = node.Line + 1 }
      | _ -> None)

  let languageErrors =
    descendants<FencedCodeBlock> document
    |> List.filter (fun code -> not (String.IsNullOrEmpty code.Info) && (Highlight.tryLanguage code.Info).IsNone)
    |> List.map (fun code ->
      errorAt file code.Line $"The code block has the unknown language '{code.Info}'. Use one of these languages: {Highlight.names}.")

  let errors =
    rawHtmlErrors file document @ linkErrors @ languageErrors |> List.sortBy _.Line

  if errors.IsEmpty then
    Ok
      { Html = render document
        Headings =
          List.zip3 headings ids texts
          |> List.map (fun (heading, id, text) -> { Level = heading.Level; Id = id; Text = text })
        Anchors = Set.ofList ids
        Links = internalLinks
        ReadingTime = readingTime (blockText document) }
  else
    Error errors
