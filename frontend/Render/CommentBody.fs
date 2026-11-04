[<RequireQualifiedAccess>]
module TwoPoint.Site.Render.CommentBody

open Markdig
open Markdig.Parsers
open Markdig.Renderers
open Markdig.Renderers.Html
open Markdig.Syntax
open Markdig.Syntax.Inlines
open System
open System.IO

let private pipeline =
  let builder =
    MarkdownPipelineBuilder()
      .UsePreciseSourceLocation()
      .UseAutoLinks()
      .UseSoftlineBreakAsHardlineBreak()
      .DisableHtml()

  builder.BlockParsers.TryRemove<HeadingBlockParser>() |> ignore
  builder.BlockParsers.TryRemove<ThematicBreakParser>() |> ignore
  builder.BlockParsers.Find<ParagraphBlockParser>().ParseSetexHeadings <- false
  builder.Build()

type private Target =
  | External
  | OwnSite
  | Mail
  | Refused

let private target (stage: Stage) (url: string) =
  match Uri.TryCreate(url, UriKind.Absolute) with
  | true, uri when OwnHost.isUrl uri -> OwnSite
  | true, uri when Azurite.isForbidden stage uri -> Refused
  | true, uri when uri.Scheme = Uri.UriSchemeHttp || uri.Scheme = Uri.UriSchemeHttps -> External
  | true, uri when uri.Scheme = Uri.UriSchemeMailto -> Mail
  | _ -> Refused

let private openInNewTab (node: MarkdownObject) =
  let attributes = node.GetAttributes()
  attributes.AddPropertyIfNotExist("target", "_blank")
  attributes.AddPropertyIfNotExist("rel", "nofollow ugc noopener noreferrer")

let private toText (link: LinkInline) =
  for child in List.ofSeq link do
    child.Remove()
    link.InsertBefore child

  link.Remove()

let private startsWithScheme (text: string) =
  text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
  || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)

let private applyLinkRule (stage: Stage) (source: string) (node: Inline) =
  match node with
  | :? LinkInline as link when link.IsAutoLink ->
    let typed = source.Substring(link.Span.Start, link.Span.Length)

    match target stage link.Url with
    | _ when not (startsWithScheme typed) -> link.ReplaceBy(LiteralInline typed, false) |> ignore
    | Refused -> link.ReplaceBy(LiteralInline typed, false) |> ignore
    | External -> openInNewTab link
    | OwnSite
    | Mail -> ()
  | :? LinkInline as link ->
    let isInLink = link.IsImage && not (isNull (link.Parent.FirstParentOfType<LinkInline>()))
    link.IsImage <- false

    match target stage link.Url with
    | _ when isInLink -> toText link
    | Refused -> toText link
    | External -> openInNewTab link
    | OwnSite
    | Mail -> ()
  | :? AutolinkInline as link ->
    match target stage (if link.IsEmail then "mailto:" + link.Url else link.Url) with
    | Refused -> link.ReplaceBy(LiteralInline link.Url) |> ignore
    | External -> openInNewTab link
    | OwnSite
    | Mail -> ()
  | _ -> ()

/// The list parser makes a thematic break of `---` and `***` with no thematic break parser in the pipeline.
let private breakToText (source: string) (block: ThematicBreakBlock) =
  let text = LiteralInline(source.Substring(block.Span.Start, block.Span.Length))
  let parent = block.Parent
  let index = parent.IndexOf block

  match (if index > 0 then parent[index - 1] else null) with
  | :? ParagraphBlock as paragraph when source.AsSpan(paragraph.Span.End, block.Span.Start - paragraph.Span.End).Count '\n' = 1 ->
    paragraph.Inline.AppendChild(LineBreakInline(IsHard = true)).AppendChild text |> ignore
    parent.RemoveAt index
  | _ ->
    let paragraph = ParagraphBlock(Inline = ContainerInline())
    paragraph.Inline.AppendChild text |> ignore
    parent[index] <- paragraph

let private lines (block: CodeBlock) =
  [ for index in 0 .. block.Lines.Count - 1 -> block.Lines.Lines[index].Slice.ToString() ]

type private CodeRenderer() =
  inherit HtmlObjectRenderer<CodeBlock>()

  override _.Write(renderer: HtmlRenderer, block: CodeBlock) =
    renderer
      .EnsureLine()
      .Write("<pre tabindex=\"0\"><code>")
      .Write(Highlight.plain (lines block))
      .WriteLine("</code></pre>")
    |> ignore

let private descendants<'a when 'a :> MarkdownObject> (document: MarkdownDocument) =
  MarkdownObjectExtensions.Descendants<'a>(document :> MarkdownObject) |> List.ofSeq

/// The HTML of the Markdown source of a comment. Only an absolute http, https, or mailto URL makes a link.
let html (stage: Stage) (source: string) =
  let document = Markdown.Parse(source, pipeline)
  // A walk of Descendants after a change to the tree can miss nodes, so each walk comes first.
  let breaks = descendants<ThematicBreakBlock> document
  let inlines = descendants<Inline> document

  for block in breaks do
    breakToText source block

  for node in inlines do
    applyLinkRule stage source node

  use writer = new StringWriter()
  let renderer = HtmlRenderer(writer)
  pipeline.Setup renderer
  renderer.ObjectRenderers.ReplaceOrAdd<CodeBlockRenderer>(CodeRenderer()) |> ignore
  renderer.Render document |> ignore
  writer.ToString()
