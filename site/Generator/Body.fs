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

/// The text of the post as the `reading-time` count of the Astro build sees it: the text of each
/// node, code included, joined with no separator.
let rec private blockText (block: Block) =
  match block with
  | :? Extensions.Yaml.YamlFrontMatterBlock -> ""
  | :? ContainerBlock as container -> container |> Seq.map blockText |> String.concat ""
  | :? LeafBlock as leaf -> leafText leaf
  | _ -> ""

let private readingTime (text: string) =
  let words = text.Split([| ' '; '\n'; '\r'; '\t' |], StringSplitOptions.RemoveEmptyEntries).Length
  int (ceil (float words / 200.0))

let private errorAt file (line: int) message : PostError =
  { File = file
    Line = line + 1
    Message = message }

let private styleAttribute =
  Regex(@"<[A-Za-z][^>]*?\s(class|style)\b", RegexOptions.IgnoreCase)

let private rawHtmlErrors file (document: MarkdownDocument) =
  let errors line (html: string) =
    [ for found in styleAttribute.Matches html do
        let lineInHtml = html.AsSpan(0, found.Index).Count '\n'
        let attribute = found.Groups[1].Value
        errorAt file (line + lineInHtml) $"Delete the '{attribute}' attribute. A post has no class or style in raw HTML." ]

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

let private target (url: string) =
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
    Internal(Page(target url))
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

let private render (document: MarkdownDocument) =
  use writer = new StringWriter()
  let renderer = HtmlRenderer(writer)
  pipeline.Setup renderer
  renderer.Render document |> ignore
  writer.ToString()

/// Changes the document in place.
let ofDocument (file: string) (document: MarkdownDocument) : Result<Body, PostError list> =
  let headings = descendants<HeadingBlock> document
  let ids = headings |> List.map (fun heading -> inlineText heading.Inline) |> Slug.headingIds

  for heading, id in List.zip headings ids do
    heading.GetAttributes().Id <- id

  for image in descendants<LinkInline> document |> List.filter _.IsImage do
    image.GetAttributes().AddPropertyIfNotExist("loading", "lazy")

  for code in descendants<FencedCodeBlock> document do
    code.GetAttributes().Classes <- null

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

  let errors = rawHtmlErrors file document @ linkErrors |> List.sortBy _.Line

  if errors.IsEmpty then
    Ok
      { Html = render document
        Anchors = Set.ofList ids
        Links = internalLinks
        ReadingTime = readingTime (blockText document) }
  else
    Error errors
