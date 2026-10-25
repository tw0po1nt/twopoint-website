[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module TwoPoint.Site.Render.Post

open FsToolkit.ErrorHandling
open Markdig

let private parseWith
  (parseFrontMatter: int -> string -> Result<FrontMatter * Problem list, Problem list>)
  (images: IImageSize)
  (stage: Stage)
  (file: string)
  (text: string)
  : Result<Post * FileProblems, FileProblems> =
  let document = Markdown.Parse(text, Body.pipeline)
  let frontMatter = Body.frontMatter document
  let bodyStart = Body.startLine text frontMatter

  result {
    let! slug = Slug.ofFileName file

    let! block =
      frontMatter
      |> Result.requireSome [ Problem.error (Place.Post 1) "The post has no front matter. Start the file with a '---' line." ]

    let! frontMatter, frontMatterWarnings = parseFrontMatter (block.Line + 1) (block.Lines.ToString())

    let! body, bodyWarnings =
      Body.ofDocument images stage bodyStart document
      |> Result.mapError (fun problems -> frontMatterWarnings @ problems)

    return
      { File = file
        Slug = slug
        FrontMatter = frontMatter
        Body = body },
      frontMatterWarnings @ bodyWarnings
  }
  |> FileProblems.ofResult file bodyStart

/// `file` is the path from the repo root. A `series` field refers to one of `series` by its slug.
let parse (images: IImageSize) (stage: Stage) (series: Series list) (file: string) (text: string) : Result<Post * FileProblems, FileProblems> =
  parseWith (FrontMatter.parsePublished series) images stage file text

/// `previewTime` is the publish date of a draft that has none.
let parseDraft
  (images: IImageSize)
  (stage: Stage)
  (series: Series list)
  (previewTime: System.DateTimeOffset)
  (file: string)
  (text: string)
  : Result<Post * FileProblems, FileProblems> =
  parseWith (FrontMatter.parseDraft previewTime series) images stage file text

let summary (text: string) : PostSummary =
  match Body.frontMatter (Markdown.Parse(text, Body.pipeline)) with
  | Some block -> FrontMatter.parseSummary (block.Lines.ToString())
  | None -> { Title = None; PublishDate = None }

let urlPath (post: Post) = $"/posts/{post.Slug}/"

/// The image of the series wins over the image of the post.
let ogImageSource (post: Post) : System.Uri option =
  post.FrontMatter.Series
  |> Option.bind _.Image
  |> Option.orElse post.FrontMatter.Image

let ogImageProblem (url: System.Uri) (reason: string) : Problem =
  Problem.error (Place.Post 1) $"The front matter gives the Open Graph image {url}, and the build cannot make its crop: {reason}."
