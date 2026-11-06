module TwoPoint.Core.Tests.PostDraftTests

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Posts.Queries
open TwoPoint.Core.Util
open TwoPoint.Site.Render

open Expecto
open FsToolkit.ErrorHandling
open IcedTasks

open System
open System.Threading

let private utcNow = DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)

let private slug value = Slug.Unsafe.create value

/// A post store in memory. A blob has the ETag "v1", "v2", and so on, and a write checks its condition like Azure does.
type private FakeStore(published : Map<string, string>, working : Map<string, string * int>) =
  let mutable working = working
  let mutable beforeWrite = ignore
  let mutable beforeDelete = ignore
  let mutable images : IImageSize =
    { new IImageSize with
        member _.Size _ = Result.Ok { Width = 1600; Height = 900 } }
  let mutable storeDown = false
  let mutable deliveryStart = NotStarted

  member _.Working = working |> Map.map (fun _ (text, _) -> text)

  member _.BeforeWrite with set action = beforeWrite <- action

  member _.BeforeDelete with set action = beforeDelete <- action

  member _.Images with set value = images <- value

  member _.StoreDown with set value = storeDown <- value

  member _.DeliveryStart with set value = deliveryStart <- value

  member _.Overwrite (name : string) (text : string) =
    let version = working |> Map.tryFind name |> Option.map snd |> Option.defaultValue 0
    working <- working |> Map.add name (text, version + 1)

  interface GetPostDocument.Dependencies with
    member _.GetUtcNow () = utcNow
    member _.GetPublishedBlob slug =
      published |> Map.tryFind (Slug.value slug) |> Option.map (fun text -> { Text = text; ETag = "p1"; Mark = NotMigrated }) |> CancellableTaskResult.singleton
    member _.GetWorkingBlob slug =
      working
      |> Map.tryFind (Slug.value slug)
      |> Option.map (fun (text, version) -> { Text = text; ETag = $"v{version}"; Mark = NotMigrated })
      |> CancellableTaskResult.singleton
    member _.GetDeliveryStart _ = CancellableTaskResult.singleton deliveryStart

  interface SaveDraft.Dependencies with
    member _.Stage = Stage.Staging
    member _.Images = images
    member _.GetUtcNow () = utcNow
    member _.ParsePublished = PostCheck.parsePublished Stage.Staging
    member _.GetPublishedPosts () =
      if storeDown then
        CancellableTaskResult.error { Dependency = Dependency.BlobStorage; Type = Unknown (exn "The store is down") }
      else
        published
        |> Map.toList
        |> List.map (fun (name, text) -> { StoredPost.Slug = slug name; Text = text; ETag = "p1" })
        |> CancellableTaskResult.singleton
    member _.GetSeriesRecords () = CancellableTaskResult.singleton []
    member this.GetPublishedBlob slug = (this :> GetPostDocument.Dependencies).GetPublishedBlob slug
    member this.GetWorkingBlob slug = (this :> GetPostDocument.Dependencies).GetWorkingBlob slug
    member _.WriteWorkingBlob slug text condition =
      beforeWrite ()
      let name = Slug.value slug
      let current = working |> Map.tryFind name

      let allowed =
        match condition, current with
        | MustNotExist, None -> true
        | MatchesETag etag, Some(_, version) -> etag = $"v{version}"
        | _ -> false

      if allowed then
        let version = current |> Option.map snd |> Option.defaultValue 0 |> (+) 1
        working <- working |> Map.add name (text, version)
        CancellableTaskResult.singleton (Written $"v{version}")
      else
        CancellableTaskResult.singleton ConditionFailed

  interface ChangeSlug.Dependencies with
    member this.GetPostSlugs () = (this :> CreateDraft.Dependencies).GetPostSlugs ()
    member this.GetPublishedBlob slug = (this :> GetPostDocument.Dependencies).GetPublishedBlob slug
    member this.GetWorkingBlob slug = (this :> GetPostDocument.Dependencies).GetWorkingBlob slug
    member this.WriteWorkingBlob slug text condition = (this :> SaveDraft.Dependencies).WriteWorkingBlob slug text condition
    member _.DeleteWorkingBlob slug etag =
      beforeDelete ()
      let name = Slug.value slug
      let current = working |> Map.tryFind name

      let allowed =
        match current with
        | Some(_, version) -> etag = $"v{version}"
        | None -> false

      if allowed then
        working <- working |> Map.remove name
        CancellableTaskResult.singleton Deleted
      else
        CancellableTaskResult.singleton DeleteConditionFailed

  interface CreateDraft.Dependencies with
    member _.GetPostSlugs () =
      Set.union (published |> Map.keys |> Set.ofSeq) (working |> Map.keys |> Set.ofSeq)
      |> Seq.map slug
      |> List.ofSeq
      |> CancellableTaskResult.singleton
    member this.WriteWorkingBlob slug text condition = (this :> SaveDraft.Dependencies).WriteWorkingBlob slug text condition

let private titleAndBody (text : string) =
  let fields = TwoPoint.Site.Render.PostText.split text
  fields.Title, fields.Body

let private run (task : CancellableTask<'a>) = (task CancellationToken.None).Result

let private store published working =
  FakeStore(Map.ofList published, working |> List.map (fun (name, text) -> name, (text, 1)) |> Map.ofList)

let private create (store : FakeStore) title =
  match run (CreateDraft.withDependencies store { Title = title }) with
  | Ok(CreateDraft.Event.Created(created, etag)) -> created, etag
  | Error error -> failtestf "Expected a draft, got %A" error

let private save (store : FakeStore) name ifMatch title body =
  run (
    SaveDraft.withDependencies
      store
      { Slug = name; IfMatch = ifMatch; Title = title; Excerpt = "An excerpt"; Body = body; Details = None }
  )

let private draftWithCategory = "---\ntitle: Old\nexcerpt: E\nauthor: A\ncategory: One-offs\n---\n"

let private publishedPost heading body =
  $"---\npublishDate: 2026-01-01T00:00:00Z\nauthor: A\ntitle: {heading}\nexcerpt: E\ncategory: One-offs\n---\n\n{body}"

let private checkOf result =
  match result with
  | Ok(SaveDraft.Event.Saved(_, check)) -> check
  | Error error -> failtestf "Expected a save, got %A" error

let private published = "---\ntitle: Live post\npublishDate: 2026-01-01T00:00:00Z\ntags:\n  - FSharp\n---\n\nLive body.\n"

[<Tests>]
let tests =
  testList "Drafts" [
    test "a new draft gets the slug of its title and a working blob with no publish date" {
      let fake = store [] []
      let created, etag = create fake "My first post"
      Expect.equal (Slug.value created) "my-first-post" "The slug"
      Expect.equal etag "v1" "The ETag"
      let text = fake.Working["my-first-post"]
      Expect.stringContains text "title: My first post" "The title"
      Expect.isFalse (text.Contains "publishDate") "No publish date"
    }

    test "a new draft with no title is called untitled" {
      let created, _ = create (store [] []) ""
      Expect.equal (Slug.value created) "untitled" "The slug"
    }

    test "a new draft gets a number when the slug is taken by a draft or by a published post" {
      let fake = store [ "taken", published ] [ "taken-2", "---\n{}\n---\n" ]
      let created, _ = create fake "Taken"
      Expect.equal (Slug.value created) "taken-3" "The next free slug"
    }

    test "a new draft keeps the signs of the title out of the slug" {
      let created, _ = create (store [] []) "Part 1: C# & F#_rocks!"
      Expect.equal (Slug.value created) "part-1-c-and-f-rocks" "The slug"
    }

    test "a save with the current ETag writes the working blob and gives the new ETag" {
      let fake = store [] [ "draft", "---\ntitle: Old\n---\n\nOld body.\n" ]
      match save fake "draft" (Some "v1") "New" "New body.\n" with
      | Ok(SaveDraft.Event.Saved(etag, _)) ->
        Expect.equal etag "v2" "The new ETag"
        Expect.equal (titleAndBody fake.Working["draft"]) ("New", "New body.\n") "The new text"
      | Error error -> failtestf "Expected a save, got %A" error
    }

    test "a save with an old ETag fails, and the store keeps the newer text" {
      let fake = store [] [ "draft", "---\ntitle: Old\n---\n\nOld body.\n" ]
      fake.Overwrite "draft" "---\ntitle: Newer\n---\n\nNewer body.\n"
      let before = fake.Working["draft"]

      match save fake "draft" (Some "v1") "From an old tab" "Old tab body.\n" with
      | Error(ActionError.Logic SaveDraft.Error.Stale) -> ()
      | other -> failtestf "Expected Stale, got %A" other

      Expect.equal fake.Working["draft"] before "The newer text stays"
    }

    test "a write that loses a race with another write fails, and the store keeps the other text" {
      let fake = store [] [ "draft", "---\ntitle: Old\n---\n\nOld body.\n" ]
      fake.BeforeWrite <- fun () -> fake.Overwrite "draft" "---\ntitle: Other tab\n---\n\nOther.\n"

      match save fake "draft" (Some "v1") "Mine" "Mine.\n" with
      | Error(ActionError.Logic SaveDraft.Error.Stale) -> ()
      | other -> failtestf "Expected Stale, got %A" other

      Expect.stringContains fake.Working["draft"] "Other tab" "The other text stays"
    }

    test "a save keeps the other front matter fields and adds no publish date to a draft" {
      let fake = store [] [ "draft", "---\ntitle: Old\ntags:\n  - FSharp\n---\n\nBody.\n" ]
      save fake "draft" (Some "v1") "New" "Body.\n" |> ignore
      Expect.stringContains fake.Working["draft"] "FSharp" "The tags stay"
      Expect.isFalse (fake.Working["draft"].Contains "publishDate") "No publish date"
    }

    test "a first save of a published post writes a pending edit from the published text and leaves the published blob" {
      let fake = store [ "live", published ] []

      match save fake "live" None "Live post, edited" "Edited body.\n" with
      | Ok(SaveDraft.Event.Saved(etag, _)) -> Expect.equal etag "v1" "The ETag of the new blob"
      | Error error -> failtestf "Expected a save, got %A" error

      let text = fake.Working["live"]
      Expect.stringContains text "publishDate: 2026-01-01T00:00:00Z" "The pending edit keeps the publish date"
      Expect.stringContains text "FSharp" "The pending edit keeps the tags"
      Expect.equal (titleAndBody text) ("Live post, edited", "Edited body.\n") "The edit"
    }

    test "a save with no ETag fails when a working blob exists" {
      let fake = store [] [ "draft", "---\ntitle: Old\n---\n" ]

      match save fake "draft" None "x" "x" with
      | Error(ActionError.Logic SaveDraft.Error.Stale) -> ()
      | other -> failtestf "Expected Stale, got %A" other
    }

    test "a save of a post that does not exist fails" {
      match save (store [] []) "missing" (Some "v1") "x" "x" with
      | Error(ActionError.Logic(SaveDraft.Error.PostNotFound missing)) -> Expect.equal (Slug.value missing) "missing" "The slug"
      | other -> failtestf "Expected PostNotFound, got %A" other
    }

    test "a save of a post whose front matter is not readable fails with the reason and keeps the stored text" {
      let broken = "---\ntitle: [\ntags:\n  - FSharp\n---\n\nBody.\n"
      let fake = store [] [ "draft", broken ]

      match save fake "draft" (Some "v1") "New" "New body.\n" with
      | Error(ActionError.Logic(SaveDraft.Error.FrontMatterUnreadable reason)) -> Expect.isNotEmpty reason "A reason"
      | other -> failtestf "Expected FrontMatterUnreadable, got %A" other

      Expect.equal fake.Working["draft"] broken "The stored text stays"
    }

    test "a save with a bad slug is a validation error" {
      match save (store [] []) "Not A Slug" None "x" "x" with
      | Error(ActionError.Validation _) -> ()
      | other -> failtestf "Expected a validation error, got %A" other
    }

    test "a save of an image with no alt text returns a warning at its body line" {
      let fake = store [] [ "draft", draftWithCategory ]

      match checkOf (save fake "draft" (Some "v1") "Title" "Text.\n\n![](https://example.com/cat.png)\n") with
      | PostCheck.Problems problems ->
        let problem = problems |> List.find (fun problem -> problem.Message.Contains "Add alt text")
        Expect.equal problem.Severity Severity.Warning "A warning"
        Expect.equal problem.Place (Place.Body 4) "The body line of the image"
      | other -> failtestf "Expected one warning, got %A" other
    }

    test "a save that breaks a link in another published post returns an error for each link" {
      let target = publishedPost "Target" "Text.\n\n## Step\n\n## Other\n"
      let linker = publishedPost "Linker" "See [one](/posts/target/#step).\n\nSee [two](/posts/target/#other).\n"
      let fake = store [ "target", target; "linker", linker ] []

      match checkOf (save fake "target" None "Target" "Text without the steps.\n") with
      | PostCheck.Problems problems ->
        Expect.hasLength problems 2 "One error for each link"
        Expect.all problems (fun problem -> problem.Severity = Severity.Error) "Errors"
        Expect.all problems (fun problem -> problem.Message.Contains "the post 'linker'") "The post with the links"
        Expect.exists problems (fun problem -> problem.Message.Contains "id 'step'") "The first link"
        Expect.exists problems (fun problem -> problem.Message.Contains "id 'other'") "The second link"
      | other -> failtestf "Expected the errors, got %A" other
    }

    test "a draft with no category returns an error and is saved" {
      let fake = store [] [ "draft", "---\ntitle: Old\nexcerpt: E\nauthor: A\n---\n" ]

      match checkOf (save fake "draft" (Some "v1") "Title" "Text.\n") with
      | PostCheck.Problems [ problem ] ->
        Expect.equal problem.Severity Severity.Error "An error"
        Expect.equal problem.Place (Place.Field("category", 1)) "The category field"
      | other -> failtestf "Expected the category error, got %A" other

      Expect.stringContains fake.Working["draft"] "Title" "The draft is saved"
    }

    test "a draft with a category and no publish date returns no problem" {
      let fake = store [] [ "draft", draftWithCategory ]
      Expect.equal (checkOf (save fake "draft" (Some "v1") "Title" "Text.\n")) (PostCheck.Problems []) "No problem"
    }

    test "a failed image fetch returns the error of the build" {
      let fake = store [] [ "draft", draftWithCategory ]

      fake.Images <-
        { new IImageSize with
            member _.Size _ = Result.Error "HTTP 404" }

      match checkOf (save fake "draft" (Some "v1") "Title" "![A cat](https://example.com/cat.png)\n") with
      | PostCheck.Problems problems ->
        let problem = problems |> List.find (fun problem -> problem.Message.Contains "read the size of the image")
        Expect.equal problem.Severity Severity.Error "An error"
        Expect.stringContains problem.Message "HTTP 404" "The reason"
      | other -> failtestf "Expected the error, got %A" other
    }

    test "a failed read of the published posts leaves the draft saved and says that the check did not run" {
      let fake = store [] [ "draft", draftWithCategory ]
      fake.StoreDown <- true

      match checkOf (save fake "draft" (Some "v1") "Title" "Text.\n") with
      | PostCheck.Unavailable(PostCheck.StoreUnavailable _) -> ()
      | other -> failtestf "Expected StoreUnavailable, got %A" other

      Expect.stringContains fake.Working["draft"] "Title" "The draft is saved"
    }

    test "a check that throws leaves the draft saved and gives the reason" {
      let fake = store [] [ "draft", draftWithCategory ]

      fake.Images <-
        { new IImageSize with
            member _.Size _ = raise (System.IO.IOException "The connection closed") }

      match save fake "draft" (Some "v1") "Title" "![A cat](https://example.com/cat.png)\n" with
      | Ok(SaveDraft.Event.Saved(etag, PostCheck.Unavailable(PostCheck.CheckCrashed error))) ->
        Expect.equal etag "v2" "The new ETag"
        Expect.stringContains error.Message "The connection closed" "The reason"
      | other -> failtestf "Expected Saved with CheckCrashed, got %A" other

      Expect.stringContains fake.Working["draft"] "Title" "The draft is saved"
    }

    test "a draft saved with an empty title and body is kept" {
      let fake = store [] [ "draft", "---\ntitle: Old\n---\n\nOld.\n" ]
      Expect.isOk (save fake "draft" (Some "v1") "" "") "Saved"
      Expect.equal (titleAndBody fake.Working["draft"]) ("", "") "Empty"
    }
  ]

let private document (store : FakeStore) name =
  match run (GetPostDocument.withDependencies store name) with
  | Ok document -> document
  | Error error -> failtestf "Expected an answer, got %A" error

[<Tests>]
let documentTests =
  testList "Get a post" [
    test "a draft gives its text, its ETag, and no published text" {
      let fake = store [] [ "draft", "---\ntitle: A draft\n---\n\nBody.\n" ]
      let document = document fake "draft" |> Option.get
      Expect.equal document.State Draft "Draft"
      Expect.equal document.ETag (Some "v1") "The ETag of the working blob"
      Expect.equal document.PublishedText None "No published text"
      Expect.isFalse document.HasPendingEdit "No pending edit"
      Expect.stringContains document.Text "A draft" "The text"
    }

    test "a published post with a pending edit gives the pending text, the ETag of the edit, and the published text" {
      let fake = store [ "live", published ] [ "live", "---\ntitle: Edited\n---\n\nEdited.\n" ]
      let document = document fake "live" |> Option.get
      Expect.equal document.State Published "Published"
      Expect.isTrue document.HasPendingEdit "A pending edit"
      Expect.equal document.ETag (Some "v1") "The ETag of the edit"
      Expect.stringContains document.Text "Edited" "The pending text"
      Expect.equal document.PublishedText (Some published) "The published text"
    }

    test "a published post with no pending edit gives the published text and no ETag" {
      let document = document (store [ "live", published ] []) "live" |> Option.get
      Expect.equal document.ETag None "No ETag"
      Expect.equal document.Text published "The published text"
      Expect.equal document.PublishDate (Some(DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))) "The publish date"
    }

    test "a published post with a future publish date is upcoming" {
      let upcoming = "---\ntitle: Soon\npublishDate: 2026-04-01T00:00:00Z\n---\n"
      Expect.equal (document (store [ "soon", upcoming ] []) "soon" |> Option.get).State Upcoming "Upcoming"
    }

    test "a post that does not exist gives nothing" {
      Expect.isNone (document (store [] []) "missing") "Nothing"
    }

    test "a published post gives the start of its new-post delivery" {
      let fake = store [ "live", published ] []
      Expect.equal (document fake "live" |> Option.get).DeliveryStart NotStarted "Not started"

      fake.DeliveryStart <- Started utcNow
      Expect.equal (document fake "live" |> Option.get).DeliveryStart (Started utcNow) "Started"
    }
  ]

let private saveWithDetails (store : FakeStore) name ifMatch details =
  run (
    SaveDraft.withDependencies
      store
      { Slug = name; IfMatch = ifMatch; Title = "T"; Excerpt = ""; Body = "Body.\n"; Details = Some details }
  )

let private changeSlug (store : FakeStore) name ifMatch newSlug =
  run (ChangeSlug.withDependencies store { Slug = name; IfMatch = ifMatch; NewSlug = newSlug })

let private keys (fake : FakeStore) = fake.Working |> Map.keys |> List.ofSeq

[<Tests>]
let detailsTests =
  testList "Post details" [
    test "a save with details writes them into the front matter and keeps the other fields" {
      let fake = store [] [ "draft", "---\ntitle: Old\ncustom: Keep\n---\n\nBody.\n" ]

      let details =
        { TwoPoint.Site.Render.PostText.details "" with
            Category = Some "Series"
            Series = Some "my-series"
            Tags = [ "FSharp" ] }

      Expect.isOk (saveWithDetails fake "draft" (Some "v1") details) "Saved"
      Expect.equal (TwoPoint.Site.Render.PostText.details fake.Working["draft"]) details "The details"
      Expect.stringContains fake.Working["draft"] "custom: Keep" "The other field stays"
    }

    test "a save with no details keeps the details of the stored text" {
      let fake = store [] [ "draft", "---\ntitle: Old\ncategory: Dev\n---\n\nBody.\n" ]
      Expect.isOk (save fake "draft" (Some "v1") "New" "Body.\n") "Saved"
      Expect.equal (TwoPoint.Site.Render.PostText.details fake.Working["draft"]).Category (Some "Dev") "The category stays"
    }
  ]

[<Tests>]
let slugTests =
  testList "Change the slug" [
    test "a draft moves to the new slug, and the old working blob goes" {
      let fake = store [] [ "old-slug", "---\ntitle: A\n---\n\nBody.\n" ]

      match changeSlug fake "old-slug" (Some "v1") "new-slug" with
      | Ok(ChangeSlug.Event.Changed(changed, etag)) ->
        Expect.equal (Slug.value changed) "new-slug" "The new slug"
        Expect.equal etag "v1" "The ETag of the new blob"
      | Error error -> failtestf "Expected a change, got %A" error

      Expect.equal (keys fake) [ "new-slug" ] "The old blob is gone"
      Expect.stringContains fake.Working["new-slug"] "title: A" "The text moved"
    }

    test "a published post keeps its slug, and the store stays as it was" {
      let fake = store [ "live", published ] [ "live", "---\ntitle: Edit\n---\n" ]

      match changeSlug fake "live" (Some "v1") "other" with
      | Error(ActionError.Logic(ChangeSlug.Error.PostPublished refused)) -> Expect.equal (Slug.value refused) "live" "The slug"
      | other -> failtestf "Expected PostPublished, got %A" other

      Expect.equal (keys fake) [ "live" ] "No blob moved"
    }

    test "a published post with no pending edit keeps its slug" {
      match changeSlug (store [ "live", published ] []) "live" None "other" with
      | Error(ActionError.Logic(ChangeSlug.Error.PostPublished _)) -> ()
      | other -> failtestf "Expected PostPublished, got %A" other
    }

    test "a slug that a draft or a published post has is refused" {
      let fake = store [ "live", published ] [ "a", "---\n{}\n---\n"; "b", "---\n{}\n---\n" ]

      for taken in [ "b"; "live" ] do
        match changeSlug fake "a" (Some "v1") taken with
        | Error(ActionError.Logic(ChangeSlug.Error.SlugTaken refused)) -> Expect.equal (Slug.value refused) taken "The slug"
        | other -> failtestf "Expected SlugTaken, got %A" other

      Expect.equal (keys fake) [ "a"; "b" ] "No blob moved"
    }

    test "a draft with an old ETag is stale" {
      let fake = store [] [ "a", "---\n{}\n---\n" ]
      fake.Overwrite "a" "---\ntitle: Newer\n---\n"

      match changeSlug fake "a" (Some "v1") "b" with
      | Error(ActionError.Logic ChangeSlug.Error.Stale) -> ()
      | other -> failtestf "Expected Stale, got %A" other
    }

    test "a change that loses a race with a save keeps the newer text under the old slug" {
      let fake = store [] [ "a", "---\ntitle: Old\n---\n" ]
      fake.BeforeDelete <- fun () -> fake.Overwrite "a" "---\ntitle: Newer\n---\n"

      match changeSlug fake "a" (Some "v1") "b" with
      | Error(ActionError.Logic ChangeSlug.Error.Stale) -> ()
      | other -> failtestf "Expected Stale, got %A" other

      Expect.equal (keys fake) [ "a" ] "Only the old slug stays"
      Expect.stringContains fake.Working["a"] "Newer" "The newer text"
    }

    test "the same slug changes nothing" {
      let fake = store [] [ "a", "---\n{}\n---\n" ]

      match changeSlug fake "a" (Some "v1") "a" with
      | Ok(ChangeSlug.Event.Changed(changed, etag)) ->
        Expect.equal (Slug.value changed) "a" "The slug"
        Expect.equal etag "v1" "The ETag"
      | Error error -> failtestf "Expected no change, got %A" error
    }

    test "a draft that does not exist is not found" {
      match changeSlug (store [] []) "missing" None "b" with
      | Error(ActionError.Logic(ChangeSlug.Error.PostNotFound _)) -> ()
      | other -> failtestf "Expected PostNotFound, got %A" other
    }

    test "a bad new slug is a validation error" {
      match changeSlug (store [] [ "a", "---\n{}\n---\n" ]) "a" (Some "v1") "Not A Slug" with
      | Error(ActionError.Validation _) -> ()
      | other -> failtestf "Expected a validation error, got %A" other
    }
  ]
