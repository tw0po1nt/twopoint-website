module TwoPoint.Site.Generator.Tests.Fixture

open TwoPoint.Site.Generator

open Expecto
open System
open System.Text

/// Lines 2 to 6 of a fixture post.
let frontMatter =
  "publishDate: 2026-02-04T13:00:00Z\n\
   author: Matthew Watt\n\
   title: A post\n\
   excerpt: An excerpt\n\
   category: One-offs"

/// The body starts on line 9.
let text (frontMatter: string) (body: string) = $"---\n{frontMatter}\n---\n\n{body}\n"

let file slug = $"site/content/posts/{slug}.md"

let parse slug text = Post.parse (file slug) text

let published slug text =
  match parse slug text with
  | Ok(Published post) -> post
  | other -> failtest $"The fixture post is not published: %A{other}"

let errors slug text =
  match parse slug text with
  | Error errors -> errors
  | other -> failtest $"The fixture post has no error: %A{other}"

let errorAt slug line message : PostError =
  { File = file slug
    Line = line
    Message = message }

let renderMap stage posts =
  match RenderMap.create stage posts with
  | Ok map -> map
  | Error errors -> failtest $"The render map has errors: %A{errors}"

let siteText (map: RenderMap) path =
  match map.TryFind(SiteFile path) with
  | Some render -> Encoding.UTF8.GetString(render ())
  | None -> failtest $"The output has no '{path}'."

let realRoot =
  lazy
    (match Site.root AppContext.BaseDirectory with
     | Ok root -> root
     | Error message -> failtest message)

let realPosts =
  lazy
    (match Site.loadPosts realRoot.Value with
     | Ok posts -> posts
     | Error errors -> failtest (errors |> List.map PostError.format |> String.concat "\n"))
