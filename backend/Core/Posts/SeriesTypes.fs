namespace TwoPoint.Core.Posts

open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

open System

type SeriesReference =
  /// The published text refers to the series. An upcoming post counts.
  | LivePost of state: PostState * publishDate: DateTime option
  | DraftPost
  /// Only the pending edit of a published post refers to the series.
  | PendingEditOnly

type SeriesPost =
  { Slug : Slug
    Title : string
    Reference : SeriesReference }

type SeriesListing =
  { Slug : Slug
    Title : string
    Excerpt : string
    Image : Uri option
    ETag : string
    Posts : SeriesPost list }

type SeriesReads =
  abstract member GetUtcNow: unit -> DateTime
  abstract member GetSeriesRecords: unit -> DependencyResult<StoredSeries list>
  abstract member GetPublishedPosts: unit -> DependencyResult<StoredPost list>
  abstract member GetWorkingPosts: unit -> DependencyResult<StoredPost list>

type SeriesStore =
  { Records : StoredSeries list
    Published : StoredPost list
    Working : StoredPost list }

[<RequireQualifiedAccess>]
module SeriesPosts =

  let private seriesOf (text : string) = (TwoPoint.Site.Render.PostText.details text).Series

  let private titleOf (slug : Slug) (text : string) =
    (TwoPoint.Site.Render.Post.summary text).Title |> Option.defaultValue (Slug.value slug)

  /// Each post whose published text or working text refers to the series: the live posts in publish date order, then
  /// the drafts and the pending edits by title.
  let inSeries (utcNow : DateTime) (published : StoredPost list) (working : StoredPost list) (series : Slug) : SeriesPost list =
    let series = Slug.value series
    let publishedSlugs = published |> List.map _.Slug |> Set.ofList

    let live =
      published
      |> List.filter (fun post -> seriesOf post.Text = Some series)
      |> List.map (fun post ->
        let publishDate = (TwoPoint.Site.Render.Post.summary post.Text).PublishDate |> Option.map _.UtcDateTime
        let state =
          match publishDate with
          | Some date when date > utcNow -> PostState.Upcoming
          | _ -> PostState.Published

        { Slug = post.Slug; Title = titleOf post.Slug post.Text; Reference = LivePost(state, publishDate) }, publishDate)
      |> List.sortBy (fun (post, publishDate) -> publishDate |> Option.defaultValue DateTime.MaxValue, Slug.value post.Slug)
      |> List.map fst

    let liveSlugs = live |> List.map _.Slug |> Set.ofList

    let others =
      working
      |> List.filter (fun post -> seriesOf post.Text = Some series && not (liveSlugs.Contains post.Slug))
      |> List.map (fun post ->
        { Slug = post.Slug
          Title = titleOf post.Slug post.Text
          Reference = if publishedSlugs.Contains post.Slug then PendingEditOnly else DraftPost })
      |> List.sortBy (fun post -> post.Title.ToLowerInvariant(), Slug.value post.Slug)

    live @ others

  let tryRecord (slug : Slug) (store : SeriesStore) =
    store.Records |> List.tryFind (fun record -> record.Slug = Slug.value slug)

  let read (reads : SeriesReads) : DependencyResult<SeriesStore> =
    cancellableTaskResult {
      let! records, (published, working) =
        CancellableTaskResult.parallelZip
          (reads.GetSeriesRecords())
          (CancellableTaskResult.parallelZip (reads.GetPublishedPosts()) (reads.GetWorkingPosts()))

      return { Records = records; Published = published; Working = working }
    }
