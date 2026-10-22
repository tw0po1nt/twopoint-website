[<RequireQualifiedAccess>]
module TwoPoint.Site.Render.Check

open Oxpecker.ViewEngine

/// `Article` is the meta block, the title, the excerpt, the body, and the outline. A post with an error in its parse has no article.
type CheckedPost =
  { Article: string option
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

let private brokenElsewhere (published: Post list) (post: Post) (before: Map<string, FileProblems>) (after: Map<string, FileProblems>) =
  let names =
    published
    |> List.map (fun other -> other.File, $"the post '{other.Slug}'")
    |> Map.ofList
    |> Map.add Privacy.file "the privacy page"

  [ for KeyValue(otherFile, problems) in after do
      if otherFile <> post.File then
        let earlier =
          before.TryFind otherFile
          |> Option.map (fun problems -> problems.Problems |> List.map _.Place |> Set.ofList)
          |> Option.defaultValue Set.empty

        for problem in problems.Problems do
          if not (earlier.Contains problem.Place) then
            Problem.error
              (Place.Post 1)
              $"The change breaks a link in {names[otherFile]}, on {FileProblems.describePlace problems problem.Place}: {problem.Message}" ]

/// Only the parse of the post uses `images`, so a caller can parse each post of `published` with `Check.published`.
let post
  (stage: Stage)
  (images: IImageSize)
  (series: Series list)
  (published: Post list)
  (slug: string)
  (text: string)
  : CheckedPost =
  match Post.parse images stage series (file slug) text with
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
      Fragment() {
        Article.content withNewestTitles (Blog.seriesPosition posts withNewestTitles)
        Article.outline withNewestTitles
      }

    { Article = Some(Render.toString article)
      Problems =
        warnings.Problems
        @ Rules.termTitles published post
        @ own
        @ brokenElsewhere published post (linkProblems stage published) after }
