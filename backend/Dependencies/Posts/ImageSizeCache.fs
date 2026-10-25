namespace TwoPoint.Dependencies.Posts

open TwoPoint.Site.Render

open System
open System.Collections.Concurrent

/// Keeps the size of each image. A failure stays for `failureTtl`, so an unreachable host does not delay each save.
type ImageSizeCache(fetch : IImageFetch, failureTtl : TimeSpan, now : unit -> DateTimeOffset) =

  let sizes = ConcurrentDictionary<string, Result<ImageSize, string> * DateTimeOffset>()

  interface IImageSize with
    member _.Size url =
      match sizes.TryGetValue url.AbsoluteUri with
      | true, (Ok _ as size, _) -> size
      | true, (Error _ as failure, at) when now () - at < failureTtl -> failure
      | _ ->
        let size = fetch.Get url |> Result.bind Images.size
        sizes[url.AbsoluteUri] <- (size, now ())
        size
