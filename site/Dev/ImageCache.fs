[<RequireQualifiedAccess>]
module TwoPoint.Site.Dev.ImageCache

open TwoPoint.Site.Generator

open System
open System.Collections.Concurrent
open System.IO
open System.Security.Cryptography
open System.Text

let folder = Path.Combine("site", ".cache", "images")

let fileName (url: Uri) =
  SHA256.HashData(Encoding.UTF8.GetBytes url.AbsoluteUri) |> Convert.ToHexStringLower

/// A new image at an old URL needs a manual delete of its file. The cache keeps no failure.
let disk (folder: string) (fetch: IImageFetch) =
  { new IImageFetch with
      member _.Get url =
        let path = Path.Combine(folder, fileName url)

        if File.Exists path then
          Ok(File.ReadAllBytes path)
        else
          fetch.Get url
          |> Result.map (fun bytes ->
            Directory.CreateDirectory folder |> ignore
            let partial = $"{path}.{Guid.NewGuid():N}.partial"
            File.WriteAllBytes(partial, bytes)
            File.Move(partial, path, true)
            bytes) }

/// The cache keeps no failure, so the next call tries again.
let memoize (images: IImages) =
  let sizes = ConcurrentDictionary<string, ImageSize>()
  let crops = ConcurrentDictionary<string, byte array * ImageSize>()

  let cached (cache: ConcurrentDictionary<string, 'T>) (work: Uri -> Result<'T, string>) (url: Uri) =
    match cache.TryGetValue url.AbsoluteUri with
    | true, value -> Ok value
    | false, _ ->
      work url
      |> Result.map (fun value ->
        cache[url.AbsoluteUri] <- value
        value)

  { new IImages with
      member _.Size url = cached sizes images.Size url
      member _.OgCrop url = cached crops images.OgCrop url }
