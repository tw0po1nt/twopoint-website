[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module TwoPoint.Site.Generator.Post

open Markdig
open Markdig.Extensions.Yaml
open System.IO

/// `file` is the path from the repo root.
let parse (file: string) (text: string) : Result<ParsedPost, PostError list> =
  let slug = Path.GetFileNameWithoutExtension file
  let document = Markdown.Parse(text, Body.pipeline)

  match Seq.tryHead document with
  | _ when Slug.term slug <> slug ->
    Error [ PostError.at file 1 $"Give the file a slug as its name, for example {Slug.term slug}.md." ]
  | Some(:? YamlFrontMatterBlock as block) when block.Line = 0 ->
    FrontMatter.parse file (block.Line + 1) (block.Lines.ToString())
    |> Result.bind (fun frontMatter ->
      if frontMatter.Draft then
        Ok Draft
      else
        Body.ofDocument file document
        |> Result.map (fun body ->
          Published
            { File = file
              Slug = slug
              FrontMatter = frontMatter
              Body = body }))
  | _ -> Error [ PostError.at file 1 "The post has no front matter. Start the file with a '---' line." ]

let urlPath (post: Post) = $"/posts/{post.Slug}/"
