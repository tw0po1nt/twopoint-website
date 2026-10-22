[<RequireQualifiedAccess>]
module TwoPoint.Site.Render.Check

open Oxpecker.ViewEngine

/// `Article` is the meta block, the title, the excerpt, the body, and the outline. A post with an error in its parse has no article.
type CheckedPost =
  { Article: string option
    Problems: Problem list }

let private sizeStub =
  { new IImageSize with
      member _.Size _ = Ok { Width = 0; Height = 0 } }

let private file slug = $"{slug}.md"

/// Parses a published post for the link check of another post. It downloads no image.
let published (stage: Stage) (series: Series list) (slug: string) (text: string) : Result<Post, FileProblems> =
  Post.parse sizeStub stage series (file slug) text |> Result.map fst

let private linkProblems (stage: Stage) (posts: Post list) =
  let privacy =
    match Privacy.parse sizeStub stage with
    | Ok(page, _) -> [ Links.ofPage Privacy.urlPath page ]
    | Error _ -> []

  Links.check (SitePath.all posts) (privacy @ List.map Links.ofPost posts)
  |> List.map (fun problems -> problems.File, problems)
  |> Map.ofList

/// An error on `post` for each link in another file that the change of `post` breaks.
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
          |> Option.map (fun problems -> Set.ofList problems.Problems)
          |> Option.defaultValue Set.empty

        for problem in problems.Problems do
          if not (earlier.Contains problem) then
            let line =
              match problem.Place with
              | Place.Body line -> $"body line {line}"
              | place -> $"line {FileProblems.line problems place}"

            Problem.error
              (Place.Post 1)
              $"The change breaks a link in {names[otherFile]}, on {line}: {problem.Message}" ]

/// The problems that the next build gives for the post, with the text of the post in the place of its published version.
/// Each post of `published` can come from `Check.published`. Only the parse of the post uses `images`.
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

    let article =
      Fragment() {
        Article.content post (Blog.seriesPosition posts post)
        Article.outline post
      }

    { Article = Some(Render.toString article)
      Problems =
        warnings.Problems
        @ Rules.termTitles published post
        @ own
        @ brokenElsewhere published post (linkProblems stage published) after }
