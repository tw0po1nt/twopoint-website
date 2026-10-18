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

let errorAt slug line message = PostError.at (file slug) line message

let siteText (map: RenderMap) path =
  match map.TryFind(SiteFile path) with
  | Some render -> Encoding.UTF8.GetString(render ())
  | None -> failtest $"The output has no '{path}'."

let realRoot =
  lazy
    (match Site.root AppContext.BaseDirectory with
     | Ok root -> root
     | Error message -> failtest message)

let realContent =
  lazy
    (match Site.loadContent realRoot.Value with
     | Ok content -> content
     | Error errors -> failtest (String.concat "\n" errors))

let realPosts = lazy realContent.Value.Posts

/// The real assets, with `posts`.
let content (posts: Post list) =
  { Posts = posts
    Assets = realContent.Value.Assets }

/// The site paths that are not under `_/`.
let pagePaths (map: RenderMap) =
  RenderMap.sitePaths map |> Set.filter (fun path -> not (path.StartsWith "_/"))

/// A post with the title `slug` and a body of one paragraph.
let postWith slug (publishDate: string) category (tags: string list) (series: string option) =
  let tagLines =
    match tags with
    | [] -> ""
    | tags -> "\ntags:" + (tags |> List.map (fun tag -> $"\n  - {tag}") |> String.concat "")

  let seriesLines =
    match series with
    | Some series -> $"\nseries:\n  slug: {series}\n  title: {series}\n  excerpt: A series"
    | None -> ""

  published
    slug
    (text
      $"publishDate: {publishDate}\nauthor: Matthew Watt\ntitle: {slug}\nexcerpt: An excerpt\ncategory: {category}{tagLines}{seriesLines}"
      "Text.")
