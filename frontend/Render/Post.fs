[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module TwoPoint.Site.Render.Post

open FsToolkit.ErrorHandling
open Markdig

/// `file` is the path from the repo root. A `series` field refers to one of `series` by its slug.
let parse (images: IImages) (stage: Stage) (series: Series list) (file: string) (text: string) : Result<Post * FileProblems, FileProblems> =
  let document = Markdown.Parse(text, Body.pipeline)
  let frontMatter = Body.frontMatter document
  let bodyStart = Body.startLine text frontMatter

  result {
    let! slug = Slug.ofFileName file

    let! block =
      frontMatter
      |> Result.requireSome [ Problem.error (Place.Post 1) "The post has no front matter. Start the file with a '---' line." ]

    let! frontMatter = FrontMatter.parse series (block.Line + 1) (block.Lines.ToString())
    let! body = Body.ofDocument images stage bodyStart document

    return
      { File = file
        Slug = slug
        FrontMatter = frontMatter
        Body = body },
      []
  }
  |> FileProblems.ofResult file bodyStart

let urlPath (post: Post) = $"/posts/{post.Slug}/"
