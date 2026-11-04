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

type Renderer(limit: Highlight.Limit) =
  inherit HtmlObjectRenderer<CodeBlock>()

  override _.Write(renderer: HtmlRenderer, block: CodeBlock) =
    renderer.EnsureLine() |> ignore
    let lines = lines block

    let highlighted =
      tryFenceLanguage block
      |> Option.map (fun (name, language) -> name, Highlight.html limit language lines)

    match highlighted with
    | Some(name, Highlight.Colored html) -> renderer.Write($"<pre data-lang=\"{name}\" tabindex=\"0\"><code>").Write(html) |> ignore
    | Some(_, Highlight.TimedOut)
    | None -> renderer.Write("<pre tabindex=\"0\"><code>").Write(Highlight.plain lines) |> ignore

    renderer.WriteLine "</code></pre>" |> ignore
