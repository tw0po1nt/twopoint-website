namespace TwoPoint.Site.Render

open FsToolkit.ErrorHandling
open Markdig

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
  let parse (images: IImageSize) (stage: Stage) (file: string) (text: string) : Result<MarkdownPage * FileProblems, FileProblems> =
    let document = Markdown.Parse(text, Body.pipeline)
    let frontMatter = Body.frontMatter document
    let bodyStart = Body.startLine text frontMatter

    result {
      let! block =
        frontMatter
        |> Result.requireSome [ Problem.error (Place.Post 1) "The page has no front matter. Start the file with a '---' line." ]

      let! title, description = FrontMatter.parsePage (block.Line + 1) (block.Lines.ToString())
      let! body, warnings = Body.ofDocument images stage bodyStart document

      return
        { File = file
          Title = title
          Description = description
          Body = body },
        warnings
    }
    |> FileProblems.ofResult file bodyStart
