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

let private keepOrRefuse (refuse: unit -> unit) (node: Inline) target =
  match target with
  | Refused -> refuse ()
  | External -> openInNewTab node
  | OwnSite
  | Mail -> ()

let private applyLinkRule (stage: Stage) (source: string) (node: Inline) =
  let isInLink = not (isNull (node.Parent.FirstParentOfType<LinkInline>()))
  let targetOf url = if isInLink then Refused else target stage url

  match node with
  | :? LinkInline as link when link.IsAutoLink ->
    let typedText = source.Substring(link.Span.Start, link.Span.Length)
    let refuse () = link.ReplaceBy(LiteralInline typedText, false) |> ignore
    keepOrRefuse refuse link (if startsWithScheme typedText then targetOf link.Url else Refused)
  | :? LinkInline as link ->
    link.IsImage <- false
    keepOrRefuse (fun () -> toText link) link (targetOf link.Url)
  | :? AutolinkInline as link ->
    let refuse () = link.ReplaceBy(LiteralInline link.Url) |> ignore
    keepOrRefuse refuse link (targetOf (if link.IsEmail then "mailto:" + link.Url else link.Url))
  | _ -> ()

let rec private lastParagraph (block: Block) =
  match block with
  | :? ParagraphBlock as paragraph -> Some paragraph
  | :? ContainerBlock as container when container.Count > 0 -> lastParagraph container[container.Count - 1]
  | _ -> None

let rec private extendTo (finish: int) (block: Block) =
  if not (isNull block) then
    block.Span <- SourceSpan(block.Span.Start, max block.Span.End finish)
    extendTo finish block.Parent

let private appendLine (paragraph: ParagraphBlock) (inlines: Inline list) =
  paragraph.Inline.AppendChild(LineBreakInline(IsHard = true)) |> ignore

  for node in inlines do
    node.Remove()
    paragraph.Inline.AppendChild node |> ignore

/// The list parser makes a thematic break of `***`, and of `---` after a block other than a paragraph, when the pipeline has no thematic break parser.
let private breakToText (source: string) (block: ThematicBreakBlock) =
  let text = LiteralInline(source.Substring(block.Span.Start, block.Span.Length))
  let parent = block.Parent
  let index = parent.IndexOf block
  let isNextLine (above: Block) (below: Block) = source.AsSpan(above.Span.End, below.Span.Start - above.Span.End).Count '\n' = 1
  let previous = if index > 0 then Some parent[index - 1] else None
  let next = if index + 1 < parent.Count then Some parent[index + 1] else None

  let paragraph =
    match previous |> Option.filter (fun above -> isNextLine above block) |> Option.bind lastParagraph with
    | Some paragraph ->
      appendLine paragraph [ text ]
      parent.RemoveAt index
      paragraph
    | None ->
      let paragraph = ParagraphBlock(Inline = ContainerInline(), Span = block.Span)
      paragraph.Inline.AppendChild text |> ignore
      parent[index] <- paragraph
      paragraph

  extendTo block.Span.End paragraph

  match next with
  | Some(:? ParagraphBlock as below) when isNextLine block below ->
    appendLine paragraph (List.ofSeq below.Inline)
    parent.Remove below |> ignore
    extendTo below.Span.End paragraph
  | _ -> ()

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

/// Only an absolute http, https, or mailto URL makes a link.
let html (stage: Stage) (source: string) =
  let document = Markdown.Parse(source, pipeline)
  // A walk of Descendants can miss nodes when the tree changes during the walk, so each walk comes first.
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
