namespace TwoPoint.Site.Generator

open SkiaSharp
open System
open System.Collections.Concurrent
open System.Net.Http
open System.Threading
open System.Threading.Tasks

type ImageSize = { Width: int; Height: int }

/// `UrlPath` is from the root, for example `/_/og-1a2b3c4d.jpg`.
type OgImage = { UrlPath: string; Size: ImageSize }

/// An error gives the reason in words, for example `HTTP 404`.
type IImageFetch =
  abstract Get: Uri -> Result<byte array, string>

[<RequireQualifiedAccess>]
module ImageFetch =

  let http (client: HttpClient) =
    { new IImageFetch with
        member _.Get url =
          try
            use response = client.GetAsync(url).GetAwaiter().GetResult()

            if response.IsSuccessStatusCode then
              Ok(response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult())
            else
              Error $"HTTP {int response.StatusCode}"
          with
          | :? TaskCanceledException -> Error $"no response in {client.Timeout.TotalSeconds} seconds"
          | :? HttpRequestException as error -> Error error.Message }

  let withRetries (attempts: int) (delay: TimeSpan) (fetch: IImageFetch) =
    { new IImageFetch with
        member _.Get url =
          let rec attempt count =
            match fetch.Get url with
            | Ok bytes -> Ok bytes
            | Error _ when count < attempts ->
              Thread.Sleep delay
              attempt (count + 1)
            | Error reason -> Error $"{reason}, after {attempts} attempts"

          attempt 1 }

  /// The cache keeps no failure, so the next call tries again.
  let memoize (fetch: IImageFetch) =
    let images = ConcurrentDictionary<string, byte array>()

    { new IImageFetch with
        member _.Get url =
          match images.TryGetValue url.AbsoluteUri with
          | true, bytes -> Ok bytes
          | false, _ ->
            fetch.Get url
            |> Result.map (fun bytes ->
              images[url.AbsoluteUri] <- bytes
              bytes) }

  let create () =
    http (new HttpClient(Timeout = TimeSpan.FromSeconds 30.0))
    |> withRetries 3 (TimeSpan.FromSeconds 1.0)
    |> memoize

[<RequireQualifiedAccess>]
module Images =

  let size (bytes: byte array) : Result<ImageSize, string> =
    use data = SKData.CreateCopy bytes
    use codec = SKCodec.Create data

    match codec with
    | null -> Error "the file is not an image"
    | codec ->
      Ok
        { Width = codec.Info.Width
          Height = codec.Info.Height }

  let ogSize = { Width = 1200; Height = 630 }

  /// The largest 1.91:1 area of a source of this size.
  let private cropSize (source: ImageSize) =
    if source.Width * ogSize.Height > source.Height * ogSize.Width then
      { Width = source.Height * ogSize.Width / ogSize.Height
        Height = source.Height }
    else
      { Width = source.Width
        Height = source.Width * ogSize.Height / ogSize.Width }

  /// A crop smaller than `ogSize` keeps its size.
  let ogCrop (bytes: byte array) : Result<byte array, string> =
    use source = SKImage.FromEncodedData bytes

    match source with
    | null -> Error "the file is not an image"
    | source ->
      let crop = cropSize { Width = source.Width; Height = source.Height }
      let output = if crop.Width > ogSize.Width then ogSize else crop
      use surface = SKSurface.Create(SKImageInfo(output.Width, output.Height))
      surface.Canvas.Clear SKColors.White

      surface.Canvas.DrawImage(
        source,
        SKRect.Create(float32 ((source.Width - crop.Width) / 2), float32 ((source.Height - crop.Height) / 2), float32 crop.Width, float32 crop.Height),
        SKRect.Create(float32 output.Width, float32 output.Height),
        SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
      )

      use image = surface.Snapshot()
      use data = image.Encode(SKEncodedImageFormat.Jpeg, 85)
      Ok(data.ToArray())
