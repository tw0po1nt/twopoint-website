module TwoPoint.Core.Tests.UploadAssetTests

open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Tests.PhotoMetadataTests.PhotoFixtures
open TwoPoint.Core.Util

open Expecto
open FsToolkit.ErrorHandling
open IcedTasks

open System
open System.Security.Cryptography
open System.Text
open System.Threading

let private ascii (text : string) = Encoding.ASCII.GetBytes text

let private withTail (head : byte[]) (length : int) =
  Array.append head (Array.init (length - head.Length) byte)

let private png = cleanPng
let private jpeg = cleanJpeg
let private gif = withTail (ascii "GIF89a") 64
let private webp = simpleWebP
let private pdf = withTail (ascii "%PDF-1.7") 64
let private svg = ascii "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>"

type private FakeAssets() =
  let blobs = Collections.Generic.Dictionary<string, string * byte[]>()
  let mutable writes = 0

  member _.Blobs = blobs
  member _.Writes = writes

  interface UploadAsset.Dependencies with
    member _.WriteAsset name contentType content =
      if not (blobs.ContainsKey name) then
        writes <- writes + 1
        blobs[name] <- (contentType, content)

      CancellableTaskResult.singleton (Uri $"https://assets.test/assets/{name}")

let private upload (assets : FakeAssets) (bytes : byte[]) =
  (UploadAsset.withDependencies assets { Bytes = bytes } CancellationToken.None).Result

let private uploaded (result : ActionResult<UploadAsset.Event, UploadAsset.Error>) =
  match result with
  | Ok(UploadAsset.Event.Uploaded(name, url)) -> name, url
  | Error error -> failtestf "Expected an upload, got %A" error

[<Tests>]
let tests =
  testList "Upload an asset" [
    test "an SVG is refused" {
      let assets = FakeAssets()

      match upload assets svg with
      | Error(ActionError.Logic UploadAsset.Error.UnsupportedType) -> ()
      | other -> failtestf "Expected UnsupportedType, got %A" other

      Expect.equal assets.Blobs.Count 0 "No blob"
    }

    test "text and an empty file are refused" {
      for bytes in [ ascii "hello"; [||] ] do
        match upload (FakeAssets()) bytes with
        | Error(ActionError.Logic UploadAsset.Error.UnsupportedType) -> ()
        | other -> failtestf "Expected UnsupportedType, got %A" other
    }

    test "a file above 25 MB is refused, and a file of 25 MB is accepted" {
      let assets = FakeAssets()
      let limit = 25 * 1024 * 1024

      match upload assets (withTail (ascii "%PDF-1.7") (limit + 1)) with
      | Error(ActionError.Logic UploadAsset.Error.TooLarge) -> ()
      | other -> failtestf "Expected TooLarge, got %A" other

      Expect.equal assets.Blobs.Count 0 "No blob"
      uploaded (upload assets (withTail (ascii "%PDF-1.7") limit)) |> ignore
    }

    test "each allowed type gets its extension and its content type" {
      for bytes, extension, contentType in
        [ jpeg, "jpg", "image/jpeg"
          png, "png", "image/png"
          webp, "webp", "image/webp"
          gif, "gif", "image/gif"
          pdf, "pdf", "application/pdf" ] do
        let assets = FakeAssets()
        let name, _ = uploaded (upload assets bytes)
        Expect.stringEnds name $".{extension}" "The extension"
        Expect.equal assets.Blobs[name] (contentType, bytes) "The content type and the bytes"
    }

    test "the blob name is the first 16 hex characters of the SHA-256" {
      let name, _ = uploaded (upload (FakeAssets()) (ascii "%PDF-"))
      Expect.equal name "38523c087796e5d5.pdf" "The name"
    }

    test "the blob name and the stored bytes come from the stripped photo" {
      let assets = FakeAssets()
      let name, _ = uploaded (upload assets (jpegWithMetadata (Some 6)))
      let stripped = jpegWithOrientationOnly 6
      let hash = Convert.ToHexString(SHA256.HashData stripped).ToLowerInvariant()
      Expect.equal name $"{hash.Substring(0, 16)}.jpg" "The name is the hash of the stripped bytes"
      Expect.equal assets.Blobs[name] ("image/jpeg", stripped) "The stored bytes are the stripped bytes"
    }

    test "a photo with metadata and the same photo without it give one blob name" {
      let assets = FakeAssets()
      let first, _ = uploaded (upload assets (jpegWithMetadata None))
      let second, _ = uploaded (upload assets jpeg)
      Expect.equal first second "The same name"
    }

    test "a damaged photo is refused" {
      for bytes in [ Array.truncate 20 jpeg; Array.truncate 20 png; Array.truncate 20 webp ] do
        let assets = FakeAssets()

        match upload assets bytes with
        | Error(ActionError.Logic(UploadAsset.Error.MalformedImage _)) -> ()
        | other -> failtestf "Expected MalformedImage, got %A" other

        Expect.equal assets.Blobs.Count 0 "No blob"
    }

    test "a GIF and a PDF keep their bytes" {
      for bytes in [ gif; pdf ] do
        let assets = FakeAssets()
        let name, _ = uploaded (upload assets bytes)
        Expect.equal (snd assets.Blobs[name]) bytes "Unchanged"
    }

    test "two uploads of one file give one blob name, one URL, and one write" {
      let assets = FakeAssets()
      let first = uploaded (upload assets png)
      let second = uploaded (upload assets png)
      Expect.equal first second "The same name and URL"
      Expect.equal assets.Blobs.Count 1 "One blob"
      Expect.equal assets.Writes 1 "One write"
    }

    test "two different files give two blob names" {
      let assets = FakeAssets()
      let first, _ = uploaded (upload assets png)
      let second, _ = uploaded (upload assets jpeg)
      Expect.notEqual first second "Different names"
    }
  ]
