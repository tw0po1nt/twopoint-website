[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module TwoPoint.Site.Render.Body

open Markdig
open Markdig.Extensions.SmartyPants
open Markdig.Helpers
open Markdig.Parsers
open Markdig.Renderers
open Markdig.Renderers.Html
open Markdig.Syntax
open Markdig.Syntax.Inlines
open System
open System.IO
open System.Net
open System.Text
open System.Text.RegularExpressions

type private DirectiveExtension() =
  interface IMarkdownExtension with
    member _.Setup(pipeline: MarkdownPipelineBuilder) = pipeline.BlockParsers.Add(Directive.DirectiveParser())
    member _.Setup(_: MarkdownPipeline, _: IMarkdownRenderer) = ()

let private descendants<'a when 'a :> MarkdownObject> (document: MarkdownDocument) =
  MarkdownObjectExtensions.Descendants<'a>(document :> MarkdownObject) |> List.ofSeq

let private enDash = '\u2013'

let private emDash = '\u2014'

let private opensAfter (c: char) =
  Char.IsWhiteSpace c || "([{".Contains c || c = enDash || c = emDash

let rec private opensAfterInline (node: Inline) =
  match node with
  | null
  | :? LineBreakInline -> true
  | :? SmartyPant as smartyPant -> smartyPant.Type = SmartyPantType.Dash2 || smartyPant.Type = SmartyPantType.Dash3
  | :? HtmlEntityInline as entity when entity.Transcoded.IsEmpty -> opensAfterInline entity.PreviousSibling
  | :? HtmlEntityInline as entity -> opensAfter entity.Transcoded.Text[entity.Transcoded.End]
  | :? LiteralInline as literal when literal.Content.IsEmpty -> opensAfterInline literal.PreviousSibling
  | :? LiteralInline as literal -> opensAfter literal.Content.Text[literal.Content.End]
  | _ -> false

let private closingApostrophe =
  Regex(@"\G'(?:\d\ds|(?:tis|twas|em|til|cause)(?!\p{L}))", RegexOptions.IgnoreCase)

/// Curls each quote mark that SmartyPants leaves straight, because Markdig curls a quote only in a pair.
let private educate (literal: LiteralInline) =
  let source = literal.Content.ToString()
  let escapedLength = if literal.IsFirstCharacterEscaped then 1 else 0
  let text = source.Substring(0, escapedLength) + source.Substring(escapedLength).Replace("...", "…")
  let result = StringBuilder(text.Length)

  let lookahead =
    match literal.NextSibling with
    | :? LiteralInline as next -> text + next.Content.ToString()
    | _ -> text

  for index in 0 .. text.Length - 1 do
    let opens =
      if index = 0 then
        opensAfterInline literal.PreviousSibling
      else
        opensAfter text[index - 1]

    match text[index] with
    | c when index < escapedLength -> result.Append c |> ignore
    | '\'' -> result.Append(if opens && not (closingApostrophe.IsMatch(lookahead, index)) then '‘' else '’') |> ignore
    | '"' -> result.Append(if opens then '“' else '”') |> ignore
    | c -> result.Append c |> ignore

  literal.Content <- StringSlice(result.ToString())

type private TypographyExtension() =
  interface IMarkdownExtension with
    member _.Setup(pipeline: MarkdownPipelineBuilder) =
      pipeline.add_DocumentProcessed (
        ProcessDocumentDelegate(fun document ->
          for literal in descendants<LiteralInline> document do
            match literal.Parent with
            | :? LinkInline as link when link.IsAutoLink -> ()
            | _ -> educate literal)
      )

    member _.Setup(_: MarkdownPipeline, _: IMarkdownRenderer) = ()

let private smartyPants =
  let options = SmartyPantOptions()
  options.Mapping[SmartyPantType.Dash2] <- "&mdash;"
  options

let pipeline =
  MarkdownPipelineBuilder()
    .UseYamlFrontMatter()
    .UsePreciseSourceLocation()
    .UseSmartyPants(smartyPants)
    .UsePipeTables()
    .UseEmphasisExtras(Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough)
    .UseAutoLinks()
    .Use(DirectiveExtension())
    .Use(TypographyExtension())
    .Build()

let private sourceText (smartyPant: SmartyPant) = string smartyPant.OpeningCharacter

let private shownText (smartyPant: SmartyPant) =
  WebUtility.HtmlDecode smartyPants.Mapping[smartyPant.Type]

let rec private inlineText (smartyPantText: SmartyPant -> string) (node: Inline) =
  match node with
  | :? LiteralInline as literal -> literal.Content.ToString()
  | :? CodeInline as code -> code.Content
  | :? LineBreakInline as lineBreak -> if lineBreak.IsHard then "" else "\n"
  | :? HtmlEntityInline as entity -> entity.Transcoded.ToString()
  | :? SmartyPant as smartyPant -> smartyPantText smartyPant
  | :? AutolinkInline as autolink -> autolink.Url
  | :? ContainerInline as container -> container |> Seq.map (inlineText smartyPantText) |> String.concat ""
  | _ -> ""

let private leafText (leaf: LeafBlock) =
  match leaf with
  | :? CodeBlock as code -> code.Lines.ToString()
  | :? HtmlBlock -> ""
  | _ when isNull leaf.Inline -> ""
  | _ -> inlineText shownText leaf.Inline

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

/// The front matter block, when the text starts with one.
let frontMatter (document: MarkdownDocument) =
  match Seq.tryHead document with
  | Some(:? Extensions.Yaml.YamlFrontMatterBlock as block) when block.Line = 0 -> Some block
  | _ -> None

/// The file line of body line 1: the line after the closing `---`, or line 1 when the text has no front matter.
let startLine (text: string) (frontMatter: Extensions.Yaml.YamlFrontMatterBlock option) =
  match frontMatter with
  | Some block -> text.AsSpan(0, block.Span.End).Count '\n' + 2
  | None -> 1

/// `startLine` is the file line of body line 1, and `line` is a Markdig line, which counts from 0.
let private bodyLine (startLine: int) (line: int) = line + 2 - startLine

let private errorAt startLine (line: int) message =
  Problem.error (Place.Body(bodyLine startLine line)) message

let private attributePattern = """\s+([^\s"'>/=]+)(?:\s*=\s*(?:"[^"]*"|'[^']*'|[^\s"'=<>`]+))?"""

let private attribute = Regex(attributePattern)

let private startTag = Regex("""<([A-Za-z][\w:-]*)((?:""" + attributePattern + """)*)\s*/?>""")

let private styleAttributes = set [ "class"; "style" ]

let private rawHtmlErrors startLine (document: MarkdownDocument) =
  let errors line (html: string) =
    let lineAt index = line + html.AsSpan(0, index).Count '\n'

    [ for tag in startTag.Matches html do
        let name = tag.Groups[1].Value

        if name.Equals("a", StringComparison.OrdinalIgnoreCase) then
          errorAt startLine (lineAt tag.Index) "Write the link as a Markdown link, for example [the docs](https://example.com/)."

        if name.Equals("img", StringComparison.OrdinalIgnoreCase) then
          errorAt startLine (lineAt tag.Index) "Write the image as a Markdown image, for example ![A cat](https://example.com/cat.jpg)."

        let attributes = tag.Groups[2]

        for found in attribute.Matches attributes.Value do
          let name = found.Groups[1]

          if styleAttributes.Contains(name.Value.ToLowerInvariant()) then
            errorAt
              startLine
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

let private kind (stage: Stage) (url: string) =
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
    | true, uri when Azurite.isForbidden stage uri ->
      Invalid(Azurite.problem uri)
    | true, uri when externalSchemes.Contains uri.Scheme -> External
    | true, uri when uri.Scheme = Uri.UriSchemeMailto -> Mail
    | _ -> Invalid $"Give the link to '{url}' as a root-relative link that starts with /, or as an http or https URL."

let private links stage (document: MarkdownDocument) =
  let links =
    descendants<LinkInline> document
    |> List.filter (fun link -> not link.IsImage)
    |> List.map (fun link -> link :> Inline, kind stage link.Url)

  let autolinks =
    descendants<AutolinkInline> document |> List.map (fun link -> link :> Inline, kind stage link.Url)

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

type private DirectiveRenderer(html: Directive.Directive -> string) =
  inherit HtmlObjectRenderer<Directive.DirectiveBlock>()

  override _.Write(renderer: HtmlRenderer, block: Directive.DirectiveBlock) =
    match block.Directive with
    | Ok directive -> renderer.EnsureLine().Write(html directive) |> ignore
    | Error _ -> ()

let private render (directiveHtml: Directive.Directive -> string) (document: MarkdownDocument) =
  use writer = new StringWriter()
  let renderer = HtmlRenderer(writer)
  pipeline.Setup renderer
  renderer.ObjectRenderers.ReplaceOrAdd<CodeBlockRenderer>(CodeRenderer()) |> ignore
  renderer.ObjectRenderers.Add(DirectiveRenderer directiveHtml)
  renderer.Render document |> ignore
  writer.ToString()

/// Changes the document in place. `startLine` is the file line of body line 1. The `Ok` value has the warnings.
let ofDocument (images: IImages) (stage: Stage) (startLine: int) (document: MarkdownDocument) : Result<Body * Problem list, Problem list> =
  let headings = descendants<HeadingBlock> document
  let texts = headings |> List.map (fun heading -> inlineText shownText heading.Inline)
  let ids = headings |> List.map (fun heading -> inlineText sourceText heading.Inline) |> Slug.headingIds

  for heading, id in List.zip headings ids do
    heading.GetAttributes().Id <- id

  let imageProblems =
    descendants<LinkInline> document
    |> List.filter _.IsImage
    |> List.collect (fun image ->
      let attributes = image.GetAttributes()
      attributes.AddPropertyIfNotExist("loading", "lazy")

      let warningAt message =
        Problem.warning (Place.Body(bodyLine startLine image.Line)) message

      let altText =
        if String.IsNullOrWhiteSpace(inlineText shownText image) then
          [ warningAt "The image has no alt text. Write the alt text between the brackets, for example ![A cat](…)." ]
        else
          []

      match Uri.TryCreate(image.Url, UriKind.Absolute) with
      | true, url when Azurite.isForbidden stage url -> errorAt startLine image.Line (Azurite.problem url) :: altText
      | true, url when url.Scheme = Uri.UriSchemeHttps || Azurite.isUrl url ->
        let sizeProblems =
          match images.Size url with
          | Ok size ->
            attributes.AddProperty("width", string size.Width)
            attributes.AddProperty("height", string size.Height)
            []
          | Error reason -> [ errorAt startLine image.Line $"The build cannot read the size of the image {url}: {reason}." ]

        let assetProblems =
          if AssetContainer.isUrl stage url then
            []
          else
            [ warningAt $"The image {url} is not in the asset container. Upload the image, so that the site controls it." ]

        sizeProblems @ altText @ assetProblems
      | _ -> errorAt startLine image.Line $"Give the image '{image.Url}' as an https URL." :: altText)

  let links = links stage document

  for node, kind in links do
    if kind = External then
      openInNewTab node

  let linkErrors =
    links
    |> List.choose (function
      | node, Invalid message -> Some(errorAt startLine node.Line message)
      | _ -> None)

  let internalLinks =
    links
    |> List.choose (function
      | node, Internal target -> Some { Target = target; Line = bodyLine startLine node.Line }
      | _ -> None)

  let languageErrors =
    descendants<FencedCodeBlock> document
    |> List.filter (fun code -> not (String.IsNullOrEmpty code.Info) && (Highlight.tryLanguage code.Info).IsNone)
    |> List.map (fun code ->
      errorAt startLine code.Line $"The code block has the unknown language '{code.Info}'. Use one of these languages: {Highlight.names}.")

  let directiveErrors =
    descendants<Directive.DirectiveBlock> document
    |> List.choose (fun block ->
      match block.Directive with
      | Ok(Directive.Pdf file) when Azurite.isForbidden stage file ->
        Some(errorAt startLine block.Line (Azurite.problem file))
      | Ok _ -> None
      | Error message -> Some(errorAt startLine block.Line message))

  let problems =
    rawHtmlErrors startLine document @ imageProblems @ linkErrors @ languageErrors @ directiveErrors
    |> List.sortBy _.Place

  if not (List.exists Problem.isError problems) then
    Ok(
      { Html = render Directive.html document
        EmailHtml = render Directive.emailHtml document
        Headings =
          List.zip3 headings ids texts
          |> List.map (fun (heading, id, text) -> { Level = heading.Level; Id = id; Text = text })
        Anchors = Set.ofList ids
        Links = internalLinks
        ReadingTime = readingTime (blockText document)
        StartLine = startLine },
      problems
    )
  else
    Error problems
