[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module TwoPoint.Site.Generator.Post

open Markdig
open Markdig.Extensions.Yaml

/// `file` is the path from the repo root. A `series` field refers to one of `series` by its slug.
let parse (images: IImages) (series: Series list) (file: string) (text: string) : Result<Post, PostError list> =
  Slug.ofFileName file
  |> Result.bind (fun slug ->
    let document = Markdown.Parse(text, Body.pipeline)

    match Seq.tryHead document with
    | Some(:? YamlFrontMatterBlock as block) when block.Line = 0 ->
      FrontMatter.parse series file (block.Line + 1) (block.Lines.ToString())
      |> Result.bind (fun frontMatter ->
        Body.ofDocument images file document
        |> Result.map (fun body ->
          { File = file
            Slug = slug
            FrontMatter = frontMatter
            Body = body }))
    | _ -> Error [ PostError.at file 1 "The post has no front matter. Start the file with a '---' line." ])

let urlPath (post: Post) = $"/posts/{post.Slug}/"
