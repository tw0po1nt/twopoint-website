module TwoPoint.Site.Generator.Tests.UploadTests

open TwoPoint.Site.Generator

open Expecto
open System
open System.IO
open System.Text

type private Call =
  | CreateIfNotExists
  | Upload of Blob

type private FakeContainer(?failOn: string) =
  let calls = ResizeArray<Call>()

  member _.Calls = List.ofSeq calls

  interface IBlobContainer with
    member _.CreateIfNotExists() =
      calls.Add CreateIfNotExists
      Ok()

    member _.Upload blob =
      calls.Add(Upload blob)

      if Some blob.Name = failOn then Error "HTTP 403" else Ok()

let private newsletters =
  [ ({ Slug = "first-post"; Html = Encoding.UTF8.GetBytes "<html>first</html>" }: Disk.NewsletterFile)
    { Slug = "second-post"; Html = Encoding.UTF8.GetBytes "<html>second</html>" } ]

let private production = Some(Stage.name Stage.Production)

[<Tests>]
let tests =
  testList "Upload" [
    test "each newsletter goes to posts/<slug>/index with its bytes" {
      let container = FakeContainer()
      let names = Expect.wantOk (Upload.run container production newsletters) "A production build uploads"

      Expect.equal names [ "posts/first-post/index"; "posts/second-post/index" ] "upload gives each blob name"

      let blobs =
        container.Calls
        |> List.choose (function
          | Upload blob -> Some(blob.Name, Encoding.UTF8.GetString blob.Content)
          | CreateIfNotExists -> None)

      Expect.equal
        blobs
        [ "posts/first-post/index", "<html>first</html>"; "posts/second-post/index", "<html>second</html>" ]
        "Each blob has the bytes of its newsletter"
    }

    test "each blob is text/html with public, max-age=3600" {
      let container = FakeContainer()
      Upload.run container production newsletters |> ignore

      for call in container.Calls do
        match call with
        | Upload blob ->
          Expect.equal blob.ContentType "text/html" $"{blob.Name} has the HTML content type"
          Expect.equal blob.CacheControl "public, max-age=3600" $"{blob.Name} has the cache header"
        | CreateIfNotExists -> ()
    }

    test "upload makes the container before the first blob" {
      let container = FakeContainer()
      Upload.run container production newsletters |> ignore

      Expect.equal (List.head container.Calls) CreateIfNotExists "The first call makes the container"
      Expect.equal (container.Calls |> List.filter ((=) CreateIfNotExists)).Length 1 "upload makes the container one time"
    }

    test "a staging or local build stops with no call to the container" {
      for stage in [ Stage.Staging; Stage.Local ] do
        let container = FakeContainer()
        let error = Expect.wantError (Upload.run container (Some(Stage.name stage)) newsletters) "upload stops"

        Expect.equal
          error
          $"The build stage is {Stage.name stage}. upload sends only a production build."
          "The error names the stage"

        Expect.isEmpty container.Calls $"A {Stage.name stage} build makes no call to the container"
    }

    test "no stage marker stops with no call to the container" {
      let container = FakeContainer()
      let error = Expect.wantError (Upload.run container None newsletters) "upload stops"
      Expect.equal error "Found no stage marker. Run build --stage production before upload." "The error gives the build"
      Expect.isEmpty container.Calls "No stage marker makes no call to the container"
    }

    test "an unknown stage marker stops with no call to the container" {
      let container = FakeContainer()
      Expect.isError (Upload.run container (Some "Production") newsletters) "Stage names are lower case"
      Expect.isEmpty container.Calls "An unknown stage makes no call to the container"
    }

    test "no newsletters stops with no call to the container" {
      let container = FakeContainer()
      Expect.isError (Upload.run container production []) "upload stops"
      Expect.isEmpty container.Calls "No newsletters makes no call to the container"
    }

    test "a failed blob stops the upload and names the blob" {
      let container = FakeContainer(failOn = "posts/first-post/index")
      let error = Expect.wantError (Upload.run container production newsletters) "A failed blob fails upload"
      Expect.equal error "Cannot upload posts/first-post/index: HTTP 403" "The error names the blob and the reason"
      Expect.equal container.Calls.Length 2 "No blob goes after the failed blob"
    }

    test "upload reads the stage marker and the newsletters that build writes" {
      let folder = Path.Combine(Path.GetTempPath(), $"twopoint-upload-{Guid.NewGuid()}")

      try
        Map
          [ StageMarker, (fun () -> Encoding.UTF8.GetBytes "production")
            SiteFile "index.html", (fun () -> Encoding.UTF8.GetBytes "<html>site</html>")
            for newsletter in newsletters do
              Newsletter newsletter.Slug, (fun () -> newsletter.Html) ]
        |> Disk.write folder

        Expect.equal (Disk.readStageMarker folder) production "The stage marker gives the stage"
        Expect.equal (Disk.readNewsletters folder) newsletters "Each newsletter file gives its slug and bytes"
      finally
        Directory.Delete(folder, true)
    }

    test "an empty output folder gives no stage marker and no newsletters" {
      let folder = Path.Combine(Path.GetTempPath(), $"twopoint-upload-{Guid.NewGuid()}")
      Expect.isNone (Disk.readStageMarker folder) "No build gives no stage marker"
      Expect.isEmpty (Disk.readNewsletters folder) "No build gives no newsletters"
    }
  ]
