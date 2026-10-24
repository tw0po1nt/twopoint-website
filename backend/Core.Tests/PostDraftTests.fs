module TwoPoint.Core.Tests.PostDraftTests

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Posts.Queries
open TwoPoint.Core.Util

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

  member _.Working = working |> Map.map (fun _ (text, _) -> text)

  member _.BeforeWrite with set action = beforeWrite <- action

  member _.Overwrite (name : string) (text : string) =
    let version = working |> Map.tryFind name |> Option.map snd |> Option.defaultValue 0
    working <- working |> Map.add name (text, version + 1)

  interface GetPostDocument.Dependencies with
    member _.GetUtcNow () = utcNow
    member _.GetPublishedBlob slug =
      published |> Map.tryFind (Slug.value slug) |> Option.map (fun text -> { Text = text; ETag = "p1" }) |> CancellableTaskResult.singleton
    member _.GetWorkingBlob slug =
      working
      |> Map.tryFind (Slug.value slug)
      |> Option.map (fun (text, version) -> { Text = text; ETag = $"v{version}" })
      |> CancellableTaskResult.singleton

  interface SaveDraft.Dependencies with
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
      { Slug = name; IfMatch = ifMatch; Title = title; Excerpt = ""; Body = body }
  )

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
      | Ok(SaveDraft.Event.Saved etag) ->
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
      | Ok(SaveDraft.Event.Saved etag) -> Expect.equal etag "v1" "The ETag of the new blob"
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

    test "a save with a bad slug is a validation error" {
      match save (store [] []) "Not A Slug" None "x" "x" with
      | Error(ActionError.Validation _) -> ()
      | other -> failtestf "Expected a validation error, got %A" other
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
  ]
