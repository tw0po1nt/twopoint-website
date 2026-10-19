// Builds the Astro site and the F# site with the staging stage, and compares
// the post text of each post: the visible text, the code blocks, the image
// sources, and the link targets.
//
// Usage: dotnet fsi scripts/compare-post-text.fsx [--no-build]
//
// Run `npm ci` in frontend/ and in site/Widgets/ first. With --no-build, the
// script reads the output of the last builds. The exit code is 1 if a post has
// a difference.

#r "nuget: AngleSharp, 1.7.2"

open AngleSharp.Dom
open AngleSharp.Html.Parser
open System
open System.Diagnostics
open System.IO
open System.Text.RegularExpressions

let root = Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, ".."))

let run (dir: string) (file: string) (args: string) =
  printfn "compare-post-text: %s %s (in %s)" file args (Path.GetRelativePath(root, dir))
  use proc = Process.Start(ProcessStartInfo(file, args, WorkingDirectory = dir))
  proc.WaitForExit()

  if proc.ExitCode <> 0 then
    failwithf "%s %s failed with exit code %d" file args proc.ExitCode

if not (Array.contains "--no-build" fsi.CommandLineArgs) then
  run (Path.Combine(root, "frontend")) "npm" "run build:staging"
  run (Path.Combine(root, "site", "Widgets")) "npm" "run build"
  run root "dotnet" "run --project site/Generator -- build --stage staging"

let blocks =
  set
    [ "address"; "article"; "aside"; "blockquote"; "br"; "dd"; "details"; "div"; "dl"; "dt"
      "figcaption"; "figure"; "h1"; "h2"; "h3"; "h4"; "h5"; "h6"; "hr"; "iframe"; "li"
      "ol"; "p"; "pre"; "section"; "summary"; "table"; "td"; "th"; "tr"; "ul" ]

let hidden = set [ "pre"; "script"; "style"; "template" ]

// A block boundary is a word boundary. Astro writes no space between some
// blocks, so a plain TextContent joins the words on each side.
let rec visibleText (node: INode) : string seq =
  seq {
    match node with
    | :? IText as text -> yield text.Data
    | :? IElement as element when hidden.Contains element.LocalName -> yield " "
    | :? IElement as element ->
      let isBlock = blocks.Contains element.LocalName
      if isBlock then yield " "
      for child in element.ChildNodes do
        yield! visibleText child
      if isBlock then yield " "
    | _ -> ()
  }

let words (prose: IElement) =
  String.Concat(visibleText prose).Split((null: char array), StringSplitOptions.RemoveEmptyEntries)
  |> Array.toList

let codeBlocks (prose: IElement) =
  prose.QuerySelectorAll "pre"
  |> Seq.map (fun pre ->
    pre.TextContent.Split '\n'
    |> Seq.map (fun line -> line.TrimEnd())
    |> String.concat "\n"
    |> fun code -> code.Trim '\n')
  |> Seq.toList

let siteOrigin = Regex(@"^https?://(staging\.)?twopoint\.dev(?=/|$)")

let trailingSlash = Regex(@"(?<=.)/(?=$|[?#])")

let normalizeTarget (href: string) =
  let local = siteOrigin.Replace(href, "")
  let path = if local = "" then "/" else local
  trailingSlash.Replace(path, "")

let links (prose: IElement) =
  prose.QuerySelectorAll "a[href]"
  |> Seq.map (fun a -> normalizeTarget (a.GetAttribute "href"))
  |> Seq.toList

let images (prose: IElement) =
  prose.QuerySelectorAll "img"
  |> Seq.map (fun img -> img.GetAttribute "src")
  |> Seq.toList

/// Gives the edits that turn `before` into `after`, from a longest common
/// subsequence. Choice2Of3 is an item of `before` only, and Choice3Of3 is an
/// item of `after` only.
let diff (before: 'a list) (after: 'a list) =
  let a = List.toArray before
  let b = List.toArray after
  let lcs = Array2D.zeroCreate (a.Length + 1) (b.Length + 1)

  for i in a.Length - 1 .. -1 .. 0 do
    for j in b.Length - 1 .. -1 .. 0 do
      lcs[i, j] <- if a[i] = b[j] then lcs[i + 1, j + 1] + 1 else max lcs[i + 1, j] lcs[i, j + 1]

  let rec walk i j acc =
    if i < a.Length && j < b.Length && a[i] = b[j] then walk (i + 1) (j + 1) (Choice1Of3 a[i] :: acc)
    elif j < b.Length && (i = a.Length || lcs[i, j + 1] >= lcs[i + 1, j]) then
      walk i (j + 1) (Choice3Of3 b[j] :: acc)
    elif i < a.Length then walk (i + 1) j (Choice2Of3 a[i] :: acc)
    else List.rev acc

  walk 0 0 []

/// Groups the word edits into runs, each with up to five same words of
/// context on each side.
let wordRuns (edits: Choice<string, string, string> list) =
  let edits = List.toArray edits
  let changed = edits |> Array.map (function Choice1Of3 _ -> false | _ -> true)
  let context = 5

  let rec runs start acc =
    match Array.tryFindIndex id changed[start..] with
    | None -> List.rev acc
    | Some offset ->
      let first = start + offset
      let mutable last = first

      while last + 1 < edits.Length
          && Array.exists id changed[last + 1 .. min (edits.Length - 1) (last + 2 * context)] do
        last <- last + 1 + Array.findIndex id changed[last + 1 ..]

      let window = edits[max 0 (first - context) .. min (edits.Length - 1) (last + context)]

      let side keep =
        window
        |> Array.choose (fun edit ->
          match edit, keep with
          | Choice1Of3 word, _ -> Some word
          | Choice2Of3 word, false -> Some word
          | Choice3Of3 word, true -> Some word
          | _ -> None)
        |> String.concat " "

      runs (last + 1) ((side false, side true) :: acc)

  runs 0 []

let parser = HtmlParser()

let prose (site: string) (slug: string) =
  let file = Path.Combine(root, site, "dist", "website", "posts", slug, "index.html")
  let document = parser.ParseDocument(File.ReadAllText file)

  match document.QuerySelector "article .prose" with
  | null -> failwithf "%s has no article prose" (Path.GetRelativePath(root, file))
  | element -> element

let slugs =
  Directory.GetFiles(Path.Combine(root, "site", "content", "posts"), "*.md")
  |> Array.map Path.GetFileNameWithoutExtension
  |> Array.sort

let mutable different = 0

for slug in slugs do
  let astro = prose "frontend" slug
  let fsharp = prose "site" slug
  let report = ResizeArray<string>()

  for before, after in wordRuns (diff (words astro) (words fsharp)) do
    report.Add(sprintf "  text\n    astro:  %s\n    fsharp: %s" before after)

  let compare kind (extract: IElement -> string list) =
    for edit in diff (extract astro) (extract fsharp) do
      match edit with
      | Choice2Of3 item -> report.Add(sprintf "  %s only in astro:\n    %s" kind item)
      | Choice3Of3 item -> report.Add(sprintf "  %s only in fsharp:\n    %s" kind item)
      | Choice1Of3 _ -> ()

  compare "code block" (codeBlocks >> List.map (fun code -> code.Replace("\n", "\n    ")))
  compare "image" images
  compare "link" links

  if report.Count = 0 then
    printfn
      "%s: clean (%d words, %d code blocks, %d images, %d links)"
      slug
      (words fsharp).Length
      (codeBlocks fsharp).Length
      (images fsharp).Length
      (links fsharp).Length
  else
    different <- different + 1
    printfn "%s: %d differences" slug report.Count
    report |> Seq.iter (printfn "%s")

printfn "compare-post-text: %d of %d posts have a difference" different slugs.Length
exit (if different = 0 then 0 else 1)
