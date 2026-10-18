module TwoPoint.Site.Generator.Tests.ImagesTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto
open SkiaSharp
open System
open System.Net
open System.Net.Http
open System.Text
open System.Threading.Tasks

let private url = Uri "https://example.com/a.jpg"

let private cropSize (bytes: byte array) = Images.ogCrop bytes |> Result.bind Images.size

/// Gives the responses in order, one for each call.
let private responses (results: Result<byte array, string> list) =
  let remaining = Collections.Generic.Queue results
  recording (fetchWith (fun _ -> remaining.Dequeue()))

type private StubHandler(respond: unit -> HttpResponseMessage) =
  inherit HttpMessageHandler()

  override _.SendAsync(_, _) = Task.FromResult(respond ())

let private http (respond: unit -> HttpResponseMessage) =
  ImageFetch.http (new HttpClient(new StubHandler(respond), Timeout = TimeSpan.FromSeconds 30.0))

[<Tests>]
let tests =
  testList "Images" [
    test "a large source gives a 1200×630 crop" {
      Expect.equal (cropSize (jpeg 6000 4000 SKColors.Gray)) (Ok { Width = 1200; Height = 630 }) "A large source scales down"
    }

    test "a small source keeps its size at 1.91:1" {
      Expect.equal (cropSize (jpeg 320 180 SKColors.Gray)) (Ok { Width = 320; Height = 168 }) "The YouTube source is not scaled up"
    }

    test "a portrait source keeps its width" {
      Expect.equal (cropSize (jpeg 524 720 SKColors.Gray)) (Ok { Width = 524; Height = 275 }) "The portrait source gives the full width"
    }

    test "the crop is from the center" {
      let source =
        use bitmap = new SKBitmap(3000, 630)
        use canvas = new SKCanvas(bitmap)
        canvas.Clear SKColors.Red
        use blue = new SKPaint(Color = SKColors.Blue)
        canvas.DrawRect(SKRect.Create(900f, 0f, 1200f, 630f), blue)
        use image = SKImage.FromBitmap bitmap
        use data = image.Encode(SKEncodedImageFormat.Png, 100)
        data.ToArray()

      use crop = SKBitmap.Decode(Images.ogCrop source |> ok)
      Expect.equal (crop.Width, crop.Height) (1200, 630) "The crop has the height of the source"
      Expect.isGreaterThan (int (crop.GetPixel(600, 315).Blue)) 200 "The center of the source is the center of the crop"
      Expect.isGreaterThan (int (crop.GetPixel(10, 315).Blue)) 200 "The blue band fills the crop"
    }

    test "a file that is not an image gives an error" {
      let text = Encoding.UTF8.GetBytes "<html>Not found</html>"
      Expect.equal (Images.size text) (Error "the file is not an image") "No size"
      Expect.equal (Images.ogCrop text) (Error "the file is not an image") "No crop"
    }

    test "a fetch that fails tries again" {
      let fetch, calls = responses [ Error "HTTP 503"; Error "HTTP 503"; Ok [| 1uy |] ]
      Expect.equal ((ImageFetch.withRetries 3 TimeSpan.Zero fetch).Get url) (Ok [| 1uy |]) "The third attempt downloads"
      Expect.equal calls.Count 3 "Three attempts"
    }

    test "a fetch that fails each attempt gives the last reason" {
      let fetch, calls = responses [ Error "HTTP 503"; Error "HTTP 503"; Error "HTTP 404" ]
      Expect.equal ((ImageFetch.withRetries 3 TimeSpan.Zero fetch).Get url) (Error "HTTP 404, after 3 attempts") "The build fails"
      Expect.equal calls.Count 3 "Three attempts"
    }

    test "a memoized fetch downloads an image one time, and tries a failure again" {
      let fetch, calls = responses [ Error "HTTP 503"; Ok [| 1uy |] ]
      let fetch = ImageFetch.memoize fetch
      Expect.equal (fetch.Get url) (Error "HTTP 503") "The failure"
      Expect.equal (fetch.Get url) (Ok [| 1uy |]) "The next call tries again"
      Expect.equal (fetch.Get url) (Ok [| 1uy |]) "The image is kept"
      Expect.equal calls.Count 2 "Two downloads"
    }

    test "the HTTP fetch gives the bytes, the status, the timeout, and the request error" {
      let bytes = [| 1uy; 2uy |]
      Expect.equal ((http (fun () -> new HttpResponseMessage(HttpStatusCode.OK, Content = new ByteArrayContent(bytes)))).Get url) (Ok bytes) "The bytes"
      Expect.equal ((http (fun () -> new HttpResponseMessage(HttpStatusCode.NotFound))).Get url) (Error "HTTP 404") "The status"
      Expect.equal ((http (fun () -> raise (TaskCanceledException()))).Get url) (Error "no response in 30 seconds") "The timeout"
      Expect.equal ((http (fun () -> raise (HttpRequestException "No such host is known."))).Get url) (Error "No such host is known.") "The request error"
    }
  ]
