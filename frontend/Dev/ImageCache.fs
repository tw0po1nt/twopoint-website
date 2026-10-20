[<RequireQualifiedAccess>]
module TwoPoint.Site.Dev.ImageCache

open TwoPoint.Site.Generator

open System
open System.IO
open System.Security.Cryptography
open System.Text

let folder = Path.Combine("frontend", ".cache", "images")

let fileName (url: Uri) =
  SHA256.HashData(Encoding.UTF8.GetBytes url.AbsoluteUri) |> Convert.ToHexStringLower

/// A new image at an old URL needs a manual delete of its file.
let disk (cacheFolder: string) (fetch: IImageFetch) =
  { new IImageFetch with
      member _.Get url =
        let path = Path.Combine(cacheFolder, fileName url)

        if File.Exists path then
          Ok(File.ReadAllBytes path)
        else
          fetch.Get url
          |> Result.map (fun bytes ->
            Directory.CreateDirectory cacheFolder |> ignore
            let partial = $"{path}.{Guid.NewGuid():N}.partial"
            File.WriteAllBytes(partial, bytes)
            File.Move(partial, path, true)
            bytes) }

let memoize (images: IImages) =
  let size = Memo.byUrl images.Size
  let ogCrop = Memo.byUrl images.OgCrop

  { new IImages with
      member _.Size url = size url
      member _.OgCrop url = ogCrop url }
