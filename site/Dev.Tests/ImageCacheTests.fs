module TwoPoint.Site.Dev.Tests.ImageCacheTests

open TwoPoint.Site.Dev
open TwoPoint.Site.Generator

open Expecto
open System
open System.IO

let private url = Uri "https://example.com/a-cat.jpg"

let private fetchWith (get: Uri -> Result<byte array, string>) =
  { new IImageFetch with
      member _.Get url = get url }

let private offline = fetchWith (fun _ -> Error "no network")

let private withFolder (body: string -> unit) =
  let folder = Path.Combine(Path.GetTempPath(), $"image-cache-{Guid.NewGuid():N}")

  try
    body folder
  finally
    if Directory.Exists folder then
      Directory.Delete(folder, true)

let private counting (work: Uri -> Result<'T, string>) =
  let calls = ref 0

  (fun url ->
    calls.Value <- calls.Value + 1
    work url),
  calls

[<Tests>]
let tests =
  testList "ImageCache" [
    test "a second dev process reads the image from the disk with no network" {
      withFolder (fun folder ->
        let first = ImageCache.disk folder (fetchWith (fun _ -> Ok [| 1uy; 2uy |]))
        Expect.equal (first.Get url) (Ok [| 1uy; 2uy |]) "The first process downloads"
        let second = ImageCache.disk folder offline
        Expect.equal (second.Get url) (Ok [| 1uy; 2uy |]) "The restart needs no network")
    }

    test "the file name is the SHA-256 of the URL" {
      withFolder (fun folder ->
        (ImageCache.disk folder (fetchWith (fun _ -> Ok [| 1uy |]))).Get url |> ignore

        Expect.equal
          (Directory.GetFiles folder |> Array.map Path.GetFileName)
          [| "fe579f4b778bf72a005f7e0c2b797b935e32e2d8f6c2b6a2e38b65492d1cfa5a" |]
          "One file, named by the URL")
    }

    test "the disk cache keeps no failure" {
      withFolder (fun folder ->
        Expect.isError ((ImageCache.disk folder offline).Get url) "The download fails"
        Expect.isFalse (Directory.Exists folder && Directory.GetFiles(folder).Length > 0) "No file")
    }

    test "the memory cache does each size and crop one time, and tries a failure again" {
      let size, sizeCalls = counting (fun _ -> Ok { Width = 1; Height = 2 })
      let crop, cropCalls = counting (fun _ -> Ok([| 3uy |], { Width = 4; Height = 5 }))
      let fails = ref true

      let images =
        ImageCache.memoize
          { new IImages with
              member _.Size url =
                if fails.Value then
                  fails.Value <- false
                  Error "no network"
                else
                  size url

              member _.OgCrop url = crop url }

      Expect.isError (images.Size url) "The first size fails"

      for _ in 1..3 do
        Expect.equal (images.Size url) (Ok { Width = 1; Height = 2 }) "The size"
        Expect.equal (images.OgCrop url) (Ok([| 3uy |], { Width = 4; Height = 5 })) "The crop"

      Expect.equal (sizeCalls.Value, cropCalls.Value) (1, 1) "One call each after the failure"
    }
  ]
