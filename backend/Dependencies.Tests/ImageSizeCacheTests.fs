module TwoPoint.Dependencies.Tests.ImageSizeCacheTests

open TwoPoint.Dependencies.Posts
open TwoPoint.Site.Render

open Expecto

open System

let private png =
  Convert.FromBase64String "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg=="

let private url = Uri "https://example.com/a.png"

type private Fetch(answer : Result<byte array, string>) =
  let mutable calls = 0
  member _.Calls = calls

  interface IImageFetch with
    member _.Get _ =
      calls <- calls + 1
      answer

[<Tests>]
let tests =
  testList "ImageSizeCache" [
    test "a size is fetched once" {
      let fetch = Fetch(Ok png)
      let cache = ImageSizeCache(fetch, TimeSpan.FromMinutes 5.0, fun () -> DateTimeOffset.UnixEpoch) :> IImageSize

      Expect.equal (cache.Size url) (Ok { Width = 1; Height = 1 }) "The size"
      cache.Size url |> ignore
      Expect.equal fetch.Calls 1 "One fetch"
    }

    test "a failure stays for the time to live, and then the cache tries again" {
      let fetch = Fetch(Error "HTTP 404")
      let mutable now = DateTimeOffset.UnixEpoch
      let cache = ImageSizeCache(fetch, TimeSpan.FromMinutes 5.0, fun () -> now) :> IImageSize

      Expect.isError (cache.Size url) "A failure"
      now <- now + TimeSpan.FromMinutes 4.0
      cache.Size url |> ignore
      Expect.equal fetch.Calls 1 "The failure stays"

      now <- now + TimeSpan.FromMinutes 2.0
      cache.Size url |> ignore
      Expect.equal fetch.Calls 2 "A new fetch after the time"
    }
  ]
