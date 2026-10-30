module TwoPoint.Core.Tests.SeriesTests

open TwoPoint.Core.Deploys
open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Util

open Expecto
open FsToolkit.ErrorHandling
open IcedTasks

open System
open System.Threading

module GetSeriesQuery = TwoPoint.Core.Posts.Queries.GetSeries

let private now = DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)

let private postText (title : string) (publishDate : string option) (series : string option) =
  let date = publishDate |> Option.map (fun date -> $"publishDate: {date}\n") |> Option.defaultValue ""
  let series = series |> Option.map (fun series -> $"series: {series}\ncategory: Series\n") |> Option.defaultValue ""
  $"---\n{date}author: A\ntitle: {title}\nexcerpt: E\n{series}---\n\nA body.\n"

let private record (title : string) = $"title: {title}\nexcerpt: Posts about {title}\n"

type private FakeStore(records : (string * string) list, published : (string * string) list, working : (string * string) list) =
  let log = ResizeArray<string>()
  let mutable records = records |> List.map (fun (slug, text) -> slug, (text, "s1")) |> Map.ofList
  let mutable version = 1

  let stored (posts : (string * string) list) =
    posts |> List.map (fun (slug, text) -> { Slug = Slug.Unsafe.create slug; Text = text; ETag = "e" } : StoredPost)

  member _.Log = List.ofSeq log
  member _.Records = records |> Map.map (fun _ (text, _) -> text)

  interface SeriesReads with
    member _.GetUtcNow () = now

    member _.GetSeriesRecords () =
      records
      |> Map.toList
      |> List.map (fun (slug, (text, eTag)) -> { Slug = slug; Text = text; ETag = eTag } : StoredSeries)
      |> CancellableTaskResult.singleton

    member _.GetPublishedPosts () = CancellableTaskResult.singleton (stored published)
    member _.GetWorkingPosts () = CancellableTaskResult.singleton (stored working)

  interface SaveSeries.Dependencies with
    member _.WriteSeriesBlob slug text condition =
      let key = Slug.value slug

      let allowed =
        match condition, records |> Map.tryFind key with
        | MustNotExist, None -> true
        | MatchesETag eTag, Some(_, stored) -> eTag = stored
        | _ -> false

      if allowed then
        version <- version + 1
        log.Add $"write {key}"
        records <- records |> Map.add key (text, $"s{version}")
        CancellableTaskResult.singleton (Written $"s{version}")
      else
        CancellableTaskResult.singleton ConditionFailed

    member _.DeleteSeriesBlob slug eTag =
      let key = Slug.value slug

      match records |> Map.tryFind key with
      | Some(_, stored) when stored = eTag ->
        log.Add $"delete {key}"
        records <- records |> Map.remove key
        CancellableTaskResult.singleton Deleted
      | _ -> CancellableTaskResult.singleton DeleteConditionFailed

    member _.DispatchSite () =
      log.Add "dispatch"
      CancellableTaskResult.singleton { Production = Ok(Queued(RunNumber 7L)); Staging = Ok(Queued(RunNumber 3L)) }

  interface DeleteSeries.Dependencies with
    member this.DeleteSeriesBlob slug eTag = (this :> SaveSeries.Dependencies).DeleteSeriesBlob slug eTag

let private run (task : CancellableTask<'a>) = (task CancellationToken.None).Result

let private dto slug ifMatch : SaveSeries.Dto =
  { Slug = slug
    IfMatch = ifMatch
    NewSlug = None
    Title = "Fable Reminders"
    Excerpt = "Notes on Fable"
    Image = Some "https://twopointwebsite.blob.core.windows.net/assets/e3800544312a8f2f.jpg" }

let private save (fake : FakeStore) (dto : SaveSeries.Dto) = run (SaveSeries.withDependencies fake dto)

let private delete (fake : FakeStore) slug ifMatch =
  run (DeleteSeries.withDependencies fake { Slug = slug; IfMatch = ifMatch })

let private slugs (posts : SeriesPost list) = posts |> List.map (_.Slug >> Slug.value)

let private parsed (fake : FakeStore) slug =
  TwoPoint.Site.Render.FrontMatter.parseSeries $"{slug}.yml" fake.Records[slug]

[<Tests>]
let saveTests =
  testList "Save a series" [
    test "a save writes the record and dispatches" {
      let fake = FakeStore([ "fable", record "Fable" ], [], [])

      match save fake (dto "fable" (Some "s1")) with
      | Ok(SaveSeries.SeriesSaved saved) ->
        Expect.equal (Slug.value saved.Slug) "fable" "The slug"
        Expect.equal saved.ETag "s2" "The ETag of the new record"
        Expect.equal saved.Production (Dispatched(Queued(RunNumber 7L))) "The production run"
        Expect.isEmpty saved.Warnings "No warning"
      | other -> failtestf "Expected SeriesSaved, got %A" other

      Expect.equal fake.Log [ "write fable"; "dispatch" ] "The write, then the dispatch"

      Expect.equal
        (parsed fake "fable")
        (Ok
          { Slug = "fable"
            Title = "Fable Reminders"
            Excerpt = "Notes on Fable"
            Image = Some(Uri "https://twopointwebsite.blob.core.windows.net/assets/e3800544312a8f2f.jpg") })
        "The record has the fields"
    }

    test "a new series writes a record that did not exist" {
      let fake = FakeStore([], [], [])

      match save fake { dto "fable" None with Image = None } with
      | Ok(SaveSeries.SeriesSaved saved) -> Expect.equal (Slug.value saved.Slug) "fable" "The slug"
      | other -> failtestf "Expected SeriesSaved, got %A" other

      Expect.equal fake.Log [ "write fable"; "dispatch" ] "The write, then the dispatch"
    }

    test "a new series with the slug of another series is refused" {
      let fake = FakeStore([ "fable", record "Fable" ], [], [])

      match save fake (dto "fable" None) with
      | Error(ActionError.Logic(SaveSeries.SlugTaken _)) -> ()
      | other -> failtestf "Expected SlugTaken, got %A" other

      Expect.isEmpty fake.Log "No write and no dispatch"
    }

    test "a save of a record that changed after the page read it is refused" {
      let fake = FakeStore([ "fable", record "Fable" ], [], [])

      match save fake (dto "fable" (Some "s0")) with
      | Error(ActionError.Logic SaveSeries.SeriesChanged) -> ()
      | other -> failtestf "Expected SeriesChanged, got %A" other

      Expect.equal fake.Records["fable"] (record "Fable") "The record stays"
    }

    test "the form errors are refused together" {
      let fake = FakeStore([ "fable", record "Fable" ], [], [])

      let wrong =
        { dto "fable" (Some "s1") with
            Title = " "
            Excerpt = ""
            NewSlug = Some "Fable Notes"
            Image = Some "http://example.com/a.jpg" }

      match save fake wrong with
      | Error(ActionError.Logic(SaveSeries.Invalid problems)) ->
        Expect.equal
          problems
          [ SaveSeries.EmptyTitle; SaveSeries.EmptyExcerpt; SaveSeries.BadSlug "Fable Notes"; SaveSeries.BadImage "http://example.com/a.jpg" ]
          "Each problem"
      | other -> failtestf "Expected Invalid, got %A" other

      Expect.isEmpty fake.Log "No write"
    }

    test "a slug change of a series with a published post is refused, and names the post" {
      let fake =
        FakeStore(
          [ "fable", record "Fable" ],
          [ "first-post", postText "First post" (Some "2026-01-01T00:00:00Z") (Some "fable") ],
          []
        )

      match save fake { dto "fable" (Some "s1") with NewSlug = Some "fable-notes" } with
      | Error(ActionError.Logic(SaveSeries.SlugLocked live)) -> Expect.equal (slugs live) [ "first-post" ] "The published post"
      | other -> failtestf "Expected SlugLocked, got %A" other

      Expect.isEmpty fake.Log "No write and no dispatch"
    }

    test "an upcoming post also locks the slug" {
      let fake =
        FakeStore([ "fable", record "Fable" ], [ "soon", postText "Soon" (Some "2026-12-01T00:00:00Z") (Some "fable") ], [])

      match save fake { dto "fable" (Some "s1") with NewSlug = Some "fable-notes" } with
      | Error(ActionError.Logic(SaveSeries.SlugLocked live)) -> Expect.equal (slugs live) [ "soon" ] "The upcoming post"
      | other -> failtestf "Expected SlugLocked, got %A" other
    }

    test "a slug change of a series with only a draft is refused, and names the draft" {
      let fake = FakeStore([ "fable", record "Fable" ], [], [ "a-draft", postText "A draft" None (Some "fable") ])

      match save fake { dto "fable" (Some "s1") with NewSlug = Some "fable-notes" } with
      | Error(ActionError.Logic(SaveSeries.SlugLocked posts)) -> Expect.equal (slugs posts) [ "a-draft" ] "The draft"
      | other -> failtestf "Expected SlugLocked, got %A" other

      Expect.isEmpty fake.Log "No write and no dispatch"
    }

    test "a slug change of a series with no post writes the new record and deletes the old one" {
      let fake = FakeStore([ "fable", record "Fable" ], [], [])

      match save fake { dto "fable" (Some "s1") with NewSlug = Some "fable-notes" } with
      | Ok(SaveSeries.SeriesSaved saved) ->
        Expect.equal (Slug.value saved.Slug) "fable-notes" "The new slug"
        Expect.isEmpty saved.Warnings "No warning"
      | other -> failtestf "Expected SeriesSaved, got %A" other

      Expect.equal fake.Log [ "write fable-notes"; "delete fable"; "dispatch" ] "The steps"
      Expect.equal (fake.Records |> Map.keys |> List.ofSeq) [ "fable-notes" ] "Only the new record"
    }

    test "a slug change to the slug of another series is refused" {
      let fake = FakeStore([ "fable", record "Fable"; "notes", record "Notes" ], [], [])

      match save fake { dto "fable" (Some "s1") with NewSlug = Some "notes" } with
      | Error(ActionError.Logic(SaveSeries.SlugTaken _)) -> ()
      | other -> failtestf "Expected SlugTaken, got %A" other

      Expect.isEmpty fake.Log "No write"
    }

    test "an edit of a series that does not exist is refused" {
      match save (FakeStore([], [], [])) (dto "fable" (Some "s1")) with
      | Error(ActionError.Logic(SaveSeries.SeriesNotFound _)) -> ()
      | other -> failtestf "Expected SeriesNotFound, got %A" other
    }
  ]

[<Tests>]
let deleteTests =
  testList "Delete a series" [
    test "a delete of a series that a draft refers to is refused, and names the draft" {
      let fake = FakeStore([ "fable", record "Fable" ], [], [ "a-draft", postText "A draft" None (Some "fable") ])

      match delete fake "fable" "s1" with
      | Error(ActionError.Logic(DeleteSeries.SeriesInUse posts)) ->
        Expect.equal (posts |> List.map (fun post -> Slug.value post.Slug, post.Title, post.Reference)) [ "a-draft", "A draft", DraftPost ] "The draft"
      | other -> failtestf "Expected SeriesInUse, got %A" other

      Expect.isEmpty fake.Log "No delete"
    }

    test "a delete of a series that only a pending edit refers to is refused" {
      let fake =
        FakeStore(
          [ "fable", record "Fable" ],
          [ "live", postText "Live" (Some "2026-01-01T00:00:00Z") None ],
          [ "live", postText "Live" (Some "2026-01-01T00:00:00Z") (Some "fable") ]
        )

      match delete fake "fable" "s1" with
      | Error(ActionError.Logic(DeleteSeries.SeriesInUse posts)) ->
        Expect.equal (posts |> List.map _.Reference) [ PendingEditOnly ] "The pending edit"
      | other -> failtestf "Expected SeriesInUse, got %A" other
    }

    test "a delete of a series with no post deletes the record and dispatches nothing" {
      let fake = FakeStore([ "fable", record "Fable" ], [ "other", postText "Other" (Some "2026-01-01T00:00:00Z") None ], [])

      match delete fake "fable" "s1" with
      | Ok(DeleteSeries.SeriesDeleted _) -> ()
      | other -> failtestf "Expected SeriesDeleted, got %A" other

      Expect.equal fake.Log [ "delete fable" ] "Only the delete"
    }

    test "a delete of a record that changed after the page read it is refused" {
      let fake = FakeStore([ "fable", record "Fable" ], [], [])

      match delete fake "fable" "s0" with
      | Error(ActionError.Logic DeleteSeries.SeriesChanged) -> ()
      | other -> failtestf "Expected SeriesChanged, got %A" other

      Expect.isEmpty fake.Log "No delete"
    }
  ]

[<Tests>]
let listTests =
  testList "List the series" [
    test "each series lists its posts in publish date order, an upcoming post too, and the drafts after them" {
      let fake =
        FakeStore(
          [ "fable", record "Fable"; "notes", record "Notes" ],
          [ "second", postText "Second" (Some "2026-03-01T00:00:00Z") (Some "fable")
            "upcoming", postText "Upcoming" (Some "2026-12-01T00:00:00Z") (Some "fable")
            "first", postText "First" (Some "2026-01-01T00:00:00Z") (Some "fable")
            "elsewhere", postText "Elsewhere" (Some "2026-02-01T00:00:00Z") (Some "notes") ],
          [ "zeta-draft", postText "Zeta draft" None (Some "fable")
            "alpha-draft", postText "Alpha draft" None (Some "fable")
            "second", postText "Second" (Some "2026-03-01T00:00:00Z") (Some "fable") ]
        )

      match run (GetSeriesQuery.withDependencies fake ()) with
      | Ok [ fable; notes ] ->
        Expect.equal (fable.Title, fable.Excerpt, fable.ETag) ("Fable", "Posts about Fable", "s1") "The record"

        Expect.equal
          (fable.Posts |> List.map (fun post -> Slug.value post.Slug, post.Reference))
          [ "first", LivePost(PostState.Published, Some(DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)))
            "second", LivePost(PostState.Published, Some(DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)))
            "upcoming", LivePost(PostState.Upcoming, Some(DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc)))
            "alpha-draft", DraftPost
            "zeta-draft", DraftPost ]
          "The live posts by date, then the drafts"

        Expect.equal (slugs notes.Posts) [ "elsewhere" ] "The other series"
      | other -> failtestf "Expected two series, got %A" other
    }
  ]
