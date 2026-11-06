module TwoPoint.Site.Render.Tests.Fixture

open TwoPoint.Site.Render

open Expecto
open SkiaSharp
open System
open System.Security.Cryptography
open System.Text

let jpeg (width: int) (height: int) (color: SKColor) =
  use bitmap = new SKBitmap(width, height)
  bitmap.Erase color
  use image = SKImage.FromBitmap bitmap
  use data = image.Encode(SKEncodedImageFormat.Jpeg, 90)
  data.ToArray()

let fetchWith (get: Uri -> Result<byte array, string>) =
  { new IImageFetch with
      member _.Get url = get url }

/// Each URL gives a JPEG of 1600×900, in a color that comes from the URL.
let anyImage =
  fetchWith (fun url ->
    let hash = SHA256.HashData(Encoding.UTF8.GetBytes url.AbsoluteUri)
    Ok(jpeg 1600 900 (SKColor(hash[0], hash[1], hash[2]))))

let anyImages = Images.ofFetch anyImage

let recording (fetch: IImageFetch) =
  let calls = ResizeArray<string>()

  fetchWith (fun url ->
    calls.Add url.AbsoluteUri
    fetch.Get url),
  calls

let ok result =
  match result with
  | Ok value -> value
  | Error errors -> failtest $"%A{errors}"

/// Lines 2 to 6 of a fixture post.
let frontMatter =
  "publishDate: 2026-02-04T13:00:00Z\n\
   author: Matthew Watt\n\
   title: A post\n\
   excerpt: An excerpt\n\
   category: One-offs"

/// Lines 2 to 7 of a fixture post in the series `slug`.
let seriesFrontMatter slug =
  let category = frontMatter.Replace("One-offs", "Series")
  $"{category}\nseries: {slug}"

/// The body text starts on file line 9, which is body line 2.
let text (frontMatter: string) (body: string) = $"---\n{frontMatter}\n---\n\n{body}\n"

let file slug = $"frontend/content/posts/published/{slug}.md"

let seriesRecord slug : Series =
  { Slug = slug
    Title = slug
    Excerpt = "A series"
    Image = None }

let parseOn (stage: Stage) slug text =
  Post.parse anyImages stage [] (file slug) text

let parseIn (series: Series list) slug text =
  Post.parse anyImages Stage.Production series (file slug) text

let parse slug text = parseIn [] slug text

let publishedIn (series: Series list) slug text =
  match parseIn series slug text with
  | Ok(post, _) -> post
  | Error problems -> failtest $"The fixture post has errors: %A{problems}"

let published slug text = publishedIn [] slug text

let problemsOf (result: Result<'a * FileProblems, FileProblems>) =
  match result with
  | Error problems -> problems
  | Ok(value, _) -> failtest $"The fixture has no error: %A{value}"

/// The build output of each problem.
let errorLines result = problemsOf result |> FileProblems.format

let errorsIn (series: Series list) slug text = errorLines (parseIn series slug text)

let errors slug text = errorsIn [] slug text

let errorAt slug line message = $"{file slug}:{line}: {message}"

/// A post with the title `slug` and a body of one paragraph.
let postWith slug (publishDate: string) category (tags: string list) (series: string option) =
  let tagLines =
    match tags with
    | [] -> ""
    | tags -> "\ntags:" + (tags |> List.map (fun tag -> $"\n  - {tag}") |> String.concat "")

  let seriesLine =
    match series with
    | Some series -> $"\nseries: {series}"
    | None -> ""

  publishedIn
    (series |> Option.map seriesRecord |> Option.toList)
    slug
    (text
      $"publishDate: {publishDate}\nauthor: Matthew Watt\ntitle: {slug}\nexcerpt: An excerpt\ncategory: {category}{tagLines}{seriesLine}"
      "Text.")
