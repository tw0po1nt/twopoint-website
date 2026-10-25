namespace TwoPoint.Dependencies.Posts

open TwoPoint.Core.Posts
open TwoPoint.Site.Render

open System.Collections.Concurrent

/// A post depends on the series records, so its key also has the ETags of all records.
type PublishedParseCache(stage : Stage) =

  let seriesCache = ConcurrentDictionary<string * string, Series option>()
  let postCache = ConcurrentDictionary<string * string * string, Post option>()

  let parseSeries (record : StoredSeries) =
    seriesCache.GetOrAdd(
      (record.Slug, record.ETag),
      fun _ ->
        FrontMatter.parseSeries $"{record.Slug}.yml" record.Text |> Result.toOption
    )

  member _.Parse : PostCheck.ParsePublished =
    fun posts records ->
      let series = records |> List.choose parseSeries

      let seriesKey =
        records |> List.sortBy _.Slug |> List.map (fun record -> $"{record.Slug}:{record.ETag}") |> String.concat "|"

      let parsed =
        posts
        |> List.map (fun post ->
          let key = Slug.value post.Slug, post.ETag, seriesKey

          key,
          postCache.GetOrAdd(
            key,
            fun _ -> Check.published stage series (Slug.value post.Slug) post.Text |> Result.toOption
          ))

      let current = parsed |> List.map fst |> Set.ofList

      for key in postCache.Keys do
        if not (current.Contains key) then
          postCache.TryRemove key |> ignore

      let currentSeries = records |> List.map (fun record -> record.Slug, record.ETag) |> Set.ofList

      for key in seriesCache.Keys do
        if not (currentSeries.Contains key) then
          seriesCache.TryRemove key |> ignore

      series, parsed |> List.choose snd
