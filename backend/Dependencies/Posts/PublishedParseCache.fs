namespace TwoPoint.Dependencies.Posts

open TwoPoint.Core.Posts
open TwoPoint.Site.Render

open System.Collections.Concurrent

/// The parse of a post depends on every series record.
type PublishedParseCache(stage : Stage) =

  let seriesCache = ConcurrentDictionary<string * string, Series option>()
  let postCache = ConcurrentDictionary<string * string * string, Post option>()

  member _.Parse : PostCheck.ParsePublished =
    fun posts records ->
      let series =
        records
        |> List.choose (fun record ->
          seriesCache.GetOrAdd((record.Slug, record.ETag), fun _ -> StoredSeries.parseOne record))

      let seriesKey =
        records |> List.sortBy _.Slug |> List.map (fun record -> $"{record.Slug}:{record.ETag}") |> String.concat "|"

      let parsed =
        posts
        |> List.map (fun post ->
          let key = Slug.value post.Slug, post.ETag, seriesKey
          key, postCache.GetOrAdd(key, fun _ -> PostCheck.parsePost stage series post))

      let current = parsed |> List.map fst |> Set.ofList

      for key in postCache.Keys do
        if not (current.Contains key) then
          postCache.TryRemove key |> ignore

      let currentSeries = records |> List.map (fun record -> record.Slug, record.ETag) |> Set.ofList

      for key in seriesCache.Keys do
        if not (currentSeries.Contains key) then
          seriesCache.TryRemove key |> ignore

      series, parsed |> List.choose snd
