[<RequireQualifiedAccess>]
module TwoPoint.Site.Render.CodeBlock

open Markdig.Renderers
open Markdig.Renderers.Html
open Markdig.Syntax

let private lines (block: CodeBlock) =
  [ for index in 0 .. block.Lines.Count - 1 -> block.Lines.Lines[index].Slice.ToString() ]

let private tryFenceLanguage (block: CodeBlock) =
  match block with
  | :? FencedCodeBlock as fenced -> Highlight.tryLanguage fenced.Info |> Option.map (fun language -> fenced.Info, language)
  | _ -> None

/// A fence of a known language gets its label and its colors. Each other block is plain code with no label.
type Renderer(limit: Highlight.Limit) =
  inherit HtmlObjectRenderer<CodeBlock>()

  override _.Write(renderer: HtmlRenderer, block: CodeBlock) =
    renderer.EnsureLine() |> ignore

    let colored =
      tryFenceLanguage block
      |> Option.bind (fun (name, language) ->
        match Highlight.html limit language (lines block) with
        | Highlight.Colored html -> Some(name, html)
        | Highlight.TimedOut -> None)

    match colored with
    | Some(name, html) -> renderer.Write($"<pre data-lang=\"{name}\" tabindex=\"0\"><code>").Write(html) |> ignore
    | None -> renderer.Write("<pre tabindex=\"0\"><code>").Write(Highlight.plain (lines block)) |> ignore

    renderer.WriteLine "</code></pre>" |> ignore
