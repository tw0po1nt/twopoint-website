[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.Blog

let newestFirst (posts: Post list) =
  posts |> List.sortBy (fun post -> -post.FrontMatter.PublishDate.UtcTicks, post.Slug)

type Neighbors =
  { Older: Post option
    Newer: Post option }

/// The posts before and after `post` by publish date.
let neighbors (posts: Post list) (post: Post) =
  let sorted = newestFirst posts |> Array.ofList
  let index = sorted |> Array.findIndex (fun other -> other.Slug = post.Slug)

  { Older = Array.tryItem (index + 1) sorted
    Newer = if index > 0 then Some sorted[index - 1] else None }

let relatedScore (post: Post) (other: Post) =
  let tags (post: Post) = post.FrontMatter.Tags |> List.map _.Slug |> Set.ofList
  let category = if post.FrontMatter.Category.Slug = other.FrontMatter.Category.Slug then 5 else 0
  category + (Set.intersect (tags post) (tags other)).Count

/// A post with a score of 0 is not related.
let related (posts: Post list) (post: Post) =
  newestFirst posts
  |> List.filter (fun other -> other.Slug <> post.Slug)
  |> List.map (fun other -> other, relatedScore post other)
  |> List.filter (fun (_, score) -> score > 0)
  |> List.sortByDescending snd
  |> List.truncate 4
  |> List.map fst

/// `Position` counts from 1, in publish date order.
type SeriesPosition =
  { Series: Series
    Position: int
    Count: int }

let seriesPosition (posts: Post list) (post: Post) =
  post.FrontMatter.Series
  |> Option.map (fun series ->
    let members =
      newestFirst posts
      |> List.rev
      |> List.filter (fun other -> other.FrontMatter.Series |> Option.exists (fun s -> s.Slug = series.Slug))

    { Series = series
      Position = 1 + (members |> List.findIndex (fun other -> other.Slug = post.Slug))
      Count = members.Length })

/// The newest year first, with the newest post of each year first.
let byYear (posts: Post list) =
  newestFirst posts |> List.groupBy (fun post -> post.FrontMatter.PublishDate.UtcDateTime.Year)

/// Each series with its number of posts. The series with the newest post is first.
let series (posts: Post list) =
  newestFirst posts
  |> List.choose (fun post -> post.FrontMatter.Series)
  |> List.groupBy _.Slug
  |> List.map (fun (_, members) -> List.head members, members.Length)

/// Each tag with its number of posts. The tag with the most posts is first.
let tags (posts: Post list) =
  posts
  |> List.collect _.FrontMatter.Tags
  |> List.groupBy _.Slug
  |> List.map (fun (_, uses) -> List.head uses, uses.Length)
  |> List.sortBy (fun (tag, count) -> -count, tag.Slug)
