namespace TwoPoint.Site.Generator

open FsToolkit.ErrorHandling
open Markdig
open Markdig.Extensions.Yaml

/// A page of the site that is not a post, from a Markdown file with a title and a description.
type MarkdownPage =
  { /// The path from the repo root.
    File: string
    Title: string
    Description: string
    Body: Body }

[<RequireQualifiedAccess>]
module MarkdownPage =

  /// `file` is the path from the repo root.
  let parse (fetch: IImageFetch) (file: string) (text: string) : Result<MarkdownPage, PostError list> =
    let document = Markdown.Parse(text, Body.pipeline)

    match Seq.tryHead document with
    | Some(:? YamlFrontMatterBlock as block) when block.Line = 0 ->
      result {
        let! title, description = FrontMatter.parsePage file (block.Line + 1) (block.Lines.ToString())
        let! body = Body.ofDocument fetch file document

        return
          { File = file
            Title = title
            Description = description
            Body = body }
      }
    | _ -> Error [ PostError.at file 1 "The page has no front matter. Start the file with a '---' line." ]
