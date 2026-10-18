[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module TwoPoint.Site.Generator.Post

open Markdig
open Markdig.Extensions.Yaml
open System.IO

let private errorAt file line message : PostError =
  { File = file
    Line = line
    Message = message }

/// `file` is the path from the repo root. The slug is the file name with no extension.
let parse (file: string) (text: string) : Result<ParsedPost, PostError list> =
  let slug = Path.GetFileNameWithoutExtension file
  let document = Markdown.Parse(text, Body.pipeline)

  match Seq.tryHead document with
  | _ when Slug.term slug <> slug ->
    Error [ errorAt file 1 $"Give the file a slug as its name, for example {Slug.term slug}.md." ]
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
  | _ -> Error [ errorAt file 1 "The post has no front matter. Start the file with a '---' line." ]

/// The URL path of the post page, for example `/posts/<slug>/`.
let urlPath (post: Post) = $"/posts/{post.Slug}/"
