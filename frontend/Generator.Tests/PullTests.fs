module TwoPoint.Site.Generator.Tests.PullTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto
open System
open System.IO
open System.Text

type private FakeStore(containers: Map<string, Result<(string * string) list, string>>) =
  interface IPostStore with
    member _.Download container =
      containers
      |> Map.tryFind container
      |> Option.defaultValue (Ok [])
      |> Result.map (List.map (fun (name, text) -> name, Encoding.UTF8.GetBytes text))

let private store (published: (string * string) list) (series: (string * string) list) =
  FakeStore(Map [ PostFolder.published, Ok published; PostFolder.series, Ok series ])

let private paths (files: PostFile list) = files |> List.map _.Path

let private withTempRoot (action: string -> unit) =
  let root = Path.Combine(Path.GetTempPath(), $"twopoint-posts-{Guid.NewGuid()}")
  Directory.CreateDirectory root |> ignore

  try
    action root
  finally
    Directory.Delete(root, true)

let private pulled =
  { Published = [ { Path = file "a-post"; Bytes = Encoding.UTF8.GetBytes "A post" } ]
    Series =
      [ { Path = PostFolder.filePath PostFolder.pulled PostFolder.series "a-series.yml"
          Bytes = Encoding.UTF8.GetBytes "A series" } ] }

[<Tests>]
let tests =
  testList "Pull" [
    test "pull puts each blob of published and series in the pulled folder, in path order" {
      let files =
        Pull.run (store [ "b-post.md", "B"; "a-post.md", "A" ] [ "a-series.yml", "S" ])
        |> Result.mapError List.singleton
        |> ok

      Expect.equal
        (paths files.Published)
        [ "frontend/content/posts/published/a-post.md"; "frontend/content/posts/published/b-post.md" ]
        "Each post goes to the published folder"

      Expect.equal (paths files.Series) [ "frontend/content/posts/series/a-series.yml" ] "Each record goes to the series folder"
      Expect.equal (PostFolder.text files.Published[0]) "A" "Each file has the bytes of its blob"
    }

    test "pull fails on a blob that is not a file of its container" {
      for published, series, name in
        [ [ "a-post.yml", "A" ], [], "'a-post.yml' in 'published' is not a .md file"
          [ "posts/a-post.md", "A" ], [], "'posts/a-post.md' in 'published' is not a .md file"
          [ ".md", "A" ], [], "'.md' in 'published' is not a .md file"
          [ "a-post.md", "A" ], [ "a-series.md", "S" ], "'a-series.md' in 'series' is not a .yml file" ] do
        let error = Expect.wantError (Pull.run (store published series)) $"The blobs {published} {series} fail"
        Expect.stringContains error name "The error names the blob and its container"
    }

    test "pull fails with the container when a download fails" {
      let failing = FakeStore(Map [ PostFolder.series, Error "HTTP 403" ])
      let error = Expect.wantError (Pull.run failing) "A failed download fails"
      Expect.equal error "Cannot download the container 'series': HTTP 403" "The error names the container and the reason"
    }

    test "build with no pulled post fails and names pull" {
      withTempRoot (fun root ->
        let error = Expect.wantError (PostFolder.read root PostFolder.pulled) "An empty folder fails"

        Expect.equal
          error
          "Found no post in frontend/content/posts/published. Run pull --account <name> to download the posts from the post store."
          "The error names pull")
    }

    test "the pulled folder gives back the files that pull wrote" {
      withTempRoot (fun root ->
        PostFolder.write root pulled
        Expect.equal (PostFolder.read root PostFolder.pulled) (Ok pulled) "The folder has each pulled file")
    }

    test "a second pull removes a post that left the store" {
      withTempRoot (fun root ->
        PostFolder.write root { pulled with Published = pulled.Published @ [ { Path = file "b-post"; Bytes = [||] } ] }
        PostFolder.write root pulled
        Expect.equal (PostFolder.read root PostFolder.pulled) (Ok pulled) "Only the files of the second pull stay")
    }
  ]
