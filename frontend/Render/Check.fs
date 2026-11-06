[<RequireQualifiedAccess>]
module TwoPoint.Site.Render.Check

open Oxpecker.ViewEngine
open System

/// `Content` is the meta block, the title, the excerpt, and the body. `Outline` goes next to it.
type RenderedArticle = { Content: string; Outline: string }

/// A post with an error in its parse has no article.
type CheckedPost =
  { Article: RenderedArticle option
    Problems: Problem list }

let private noImageSize =
  { new IImageSize with
      member _.Size _ = Ok { Width = 0; Height = 0 } }

let private file slug = $"{slug}.md"

/// It downloads no image.
let published (stage: Stage) (series: Series list) (slug: string) (text: string) : Result<Post, FileProblems> =
  Post.parse noImageSize stage series (file slug) text |> Result.map fst

let private isUnderAssets (link: InternalLink) =
  match link.Target with
  | Page(path, _) -> path.StartsWith "/_/"
  | SamePage _ -> false

/// The assets, the widgets, and the `og:image` crops under `/_/` are known only to the build.
let private withoutAssetLinks (source: Links.Source) =
  { source with Body = { source.Body with Links = source.Body.Links |> List.filter (isUnderAssets >> not) } }

let private linkProblems (stage: Stage) (posts: Post list) =
  let privacy =
    match Privacy.parse noImageSize stage with
    | Ok(page, _) -> [ Links.ofPage Privacy.urlPath page ]
    | Error _ -> []

  Links.check (SitePath.all posts) (privacy @ List.map Links.ofPost posts |> List.map withoutAssetLinks)
  |> List.map (fun problems -> problems.File, problems)
  |> Map.ofList

/// The link problems of each file of `after` that `before` does not have. Each problem of a file in `counted` is new.
let private newLinkProblems
  (skipped: string)
  (counted: Set<string>)
  (before: Map<string, FileProblems>)
  (after: Map<string, FileProblems>)
  =
  [ for KeyValue(otherFile, problems) in after do
      if otherFile <> skipped then
        let earlier =
          if counted.Contains otherFile then
            Set.empty
          else
            before.TryFind otherFile
            |> Option.map (fun problems -> problems.Problems |> List.map _.Place |> Set.ofList)
            |> Option.defaultValue Set.empty

        match problems.Problems |> List.filter (fun problem -> not (earlier.Contains problem.Place)) with
        | [] -> ()
        | fresh -> otherFile, problems, fresh ]

let private breakErrors (published: Post list) (found: (string * FileProblems * Problem list) list) =
  let names =
    published
    |> List.map (fun other -> other.File, $"the post '{other.Slug}'")
    |> Map.ofList
    |> Map.add Privacy.file "the privacy page"

  [ for otherFile, problems, fresh in found do
      for problem in fresh do
        Problem.error
          (Place.Post 1)
          $"This change breaks a link in {names[otherFile]} ({FileProblems.describePlace problems problem.Place}): {problem.Message}" ]

let private brokenElsewhere (published: Post list) (changedFile: string) (before: Map<string, FileProblems>) (after: Map<string, FileProblems>) =
  newLinkProblems changedFile Set.empty before after |> breakErrors published

let private check
  (ownProblems: Post -> Problem list)
  (parse: Result<Post * FileProblems, FileProblems>)
  (stage: Stage)
  (published: Post list)
  (slug: string)
  : CheckedPost =
  match parse with
  | Error problems -> { Article = None; Problems = problems.Problems }
  | Ok(post, warnings) ->
    let posts = post :: (published |> List.filter (fun other -> other.Slug <> slug))
    let after = linkProblems stage posts

    let own =
      after.TryFind post.File
      |> Option.map _.Problems
      |> Option.defaultValue []

    let withNewestTitles = Blog.withNewestTermTitles posts |> List.find (fun other -> other.Slug = slug)

    let article =
      { Content = Render.toString (Article.content withNewestTitles (Blog.seriesPosition posts withNewestTitles))
        Outline = Render.toString (Article.outline withNewestTitles) }

    { Article = Some article
      Problems =
        warnings.Problems
        @ ownProblems post
        @ Rules.termTitles published post
        @ own
        @ brokenElsewhere published post.File (linkProblems stage published) after }

/// Only the parse of the post uses `images`, so a caller can parse each post of `published` with `Check.published`.
let post
  (stage: Stage)
  (images: IImageSize)
  (series: Series list)
  (published: Post list)
  (slug: string)
  (text: string)
  : CheckedPost =
  check (fun _ -> []) (Post.parse images stage series (file slug) text) stage published slug

let private cardImageProblems (images: IImageSize) (post: Post) =
  match Post.ogImageSource post with
  | Some url ->
    match images.Size url with
    | Ok _ -> []
    | Error reason -> [ Post.ogImageProblem url reason ]
  | None -> []

let forPublish
  (stage: Stage)
  (images: IImageSize)
  (series: Series list)
  (published: Post list)
  (slug: string)
  (text: string)
  : CheckedPost =
  check (cardImageProblems images) (Post.parse images stage series (file slug) text) stage published slug

let private missingCategory (post: Post) =
  if FrontMatter.missesCategory post.FrontMatter then
    [ Problem.error (Place.Field("category", post.FrontMatter.CategoryLine)) "The post needs a category." ]
  else
    []

/// `previewTime` is the publish date of a draft that has none.
let draft
  (stage: Stage)
  (images: IImageSize)
  (series: Series list)
  (previewTime: DateTimeOffset)
  (published: Post list)
  (slug: string)
  (text: string)
  : CheckedPost =
  check missingCategory (Post.parseDraft images stage series previewTime (file slug) text) stage published slug

/// The slugs of the published posts with a link that breaks when the post `slug` leaves the site.
let linkingPosts (stage: Stage) (published: Post list) (slug: string) : string list =
  let remaining = published |> List.filter (fun other -> other.Slug <> slug)

  let files =
    newLinkProblems (file slug) Set.empty (linkProblems stage published) (linkProblems stage remaining)
    |> List.map (fun (otherFile, _, _) -> otherFile)
    |> Set.ofList

  published |> List.filter (fun other -> files.Contains other.File) |> List.map _.Slug

/// The errors of a delete of the post `slug` that publishes each post of `edits` in place of its published text.
/// Each link problem of an edit counts. A link problem of another post counts only when it is new.
let withoutPostAndEdits (stage: Stage) (published: Post list) (slug: string) (edits: Post list) : Problem list =
  let edited = edits |> List.map _.Slug |> Set.ofList

  let result =
    edits
    @ (published |> List.filter (fun other -> other.Slug <> slug && not (edited.Contains other.Slug)))

  newLinkProblems (file slug) (edits |> List.map _.File |> Set.ofList) (linkProblems stage published) (linkProblems stage result)
  |> breakErrors published

let withoutPost (stage: Stage) (published: Post list) (slug: string) : Problem list =
  withoutPostAndEdits stage published slug []
