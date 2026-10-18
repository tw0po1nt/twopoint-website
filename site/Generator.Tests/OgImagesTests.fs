module TwoPoint.Site.Generator.Tests.OgImagesTests

open TwoPoint.Site.Generator
open TwoPoint.Site.Generator.Tests.Fixture

open Expecto
open SkiaSharp
open System.Text
open System.Text.RegularExpressions

let private seriesImage = "https://twopointwebsite.blob.core.windows.net/series/a-series.jpg"

let private postImage = "https://img.youtube.com/vi/TfznO3BYNlA/mqdefault.jpg"

let private post slug (image: string option) (series: string option) =
  let imageLine = image |> Option.map (sprintf "\nimage: %s") |> Option.defaultValue ""

  let seriesLines =
    series
    |> Option.map (sprintf "\nseries:\n  slug: a-series\n  title: A series\n  excerpt: A series\n  image: %s")
    |> Option.defaultValue ""

  published slug (text $"{frontMatter}{imageLine}{seriesLines}" "Text.")

let private tags (map: RenderMap) slug =
  let html = siteText map $"posts/{slug}/index.html"

  [ for found in Regex.Matches(html, """<meta property="og:image(?::(width|height))?" content="([^"]+)">""") ->
      found.Groups[1].Value, found.Groups[2].Value ]
  |> Map.ofList

let private ogFiles (map: RenderMap) =
  RenderMap.sitePaths map |> Set.filter (fun path -> path.StartsWith "_/og-")

[<Tests>]
let tests =
  testList "Open Graph images" [
    test "the series image comes first, then the post image, then the default image" {
      let fetch, calls = recording anyImage

      let posts =
        [ post "both" (Some postImage) (Some seriesImage)
          post "post-image" (Some postImage) None
          post "neither" None None ]

      let images = (contentWith fetch posts).OgImages
      Expect.equal (List.ofSeq calls) [ seriesImage; postImage ] "One download for each source"
      Expect.notEqual images.ByPost["both"] images.ByPost["post-image"] "The series image comes before the post image"
      Expect.equal images.ByPost["neither"] realContent.Value.Assets.DefaultImage "A post with no image gets the default image"
    }

    test "the head gives the absolute URL of the crop and the size of the written file" {
      let small = fetchWith (fun _ -> Ok(jpeg 320 180 SKColors.Gray))
      let map = RenderMap.create Stage.Staging widgets (contentWith small [ post "a-post" (Some postImage) None ])
      let tags = tags map "a-post"
      let path = Expect.wantSome (Set.toList (ogFiles map) |> List.tryExactlyOne) "The output has one crop"
      Expect.isMatch path "^_/og-[0-9a-f]{8}\\.jpg$" "The crop has a hash in its name"
      Expect.equal tags[""] $"https://staging.twopoint.dev/{path}" "og:image is the absolute URL of the crop"
      let size = Images.size (map[SiteFile path] ()) |> ok
      Expect.equal (tags["width"], tags["height"]) (string size.Width, string size.Height) "The size is the size of the file"
      Expect.equal (tags["width"], tags["height"]) ("320", "168") "A small source is not scaled up"

      Expect.stringContains
        (siteText map "posts/a-post/index.html")
        """<meta name="twitter:card" content="summary_large_image">"""
        "The card is large"
    }

    test "a post with no image gets the default image and its size" {
      let map = RenderMap.create Stage.Production widgets (content [ post "a-post" None None ])
      let image = realContent.Value.Assets.DefaultImage
      let tags = tags map "a-post"
      Expect.equal tags[""] $"https://twopoint.dev{image.UrlPath}" "og:image is the default image"
      Expect.equal (tags["width"], tags["height"]) ("1200", "630") "The size of the default image"
      Expect.isEmpty (ogFiles map) "The default image needs no crop"
      Expect.contains (RenderMap.sitePaths map) (image.UrlPath.Substring 1) "The default image is in the output"
    }

    test "the posts of a series share one file" {
      let fetch, calls = recording anyImage
      let map = RenderMap.create Stage.Local widgets (contentWith fetch [ post "part-1" None (Some seriesImage); post "part-2" None (Some seriesImage) ])
      Expect.equal calls.Count 1 "One download"
      Expect.equal (ogFiles map).Count 1 "One file"
      Expect.equal (tags map "part-1") (tags map "part-2") "Both posts have the same tags"
    }

    test "a changed source gives a new URL" {
      let urlOf color =
        let fetch = fetchWith (fun _ -> Ok(jpeg 1600 900 color))
        (contentWith fetch [ post "a-post" (Some postImage) None ]).OgImages.ByPost["a-post"].UrlPath

      Expect.notEqual (urlOf SKColors.Red) (urlOf SKColors.Blue) "New bytes give a new name"
    }

    test "a source that does not download fails the build with the post slug and the URL" {
      let failing = fetchWith (fun _ -> Error "HTTP 404, after 3 attempts")

      Expect.equal
        (OgImages.create failing realContent.Value.Assets.DefaultImage [ post "part-1" None (Some seriesImage); post "part-2" None (Some seriesImage) ])
        (Error
          [ $"The post part-1 has the Open Graph image {seriesImage}, and the build cannot make its crop: HTTP 404, after 3 attempts."
            $"The post part-2 has the Open Graph image {seriesImage}, and the build cannot make its crop: HTTP 404, after 3 attempts." ])
        "Each post of the source fails"
    }

    test "a source that is not an image fails the build with the post slug and the URL" {
      let page = fetchWith (fun _ -> Ok(Encoding.UTF8.GetBytes "<html>Not found</html>"))

      Expect.equal
        (OgImages.create page realContent.Value.Assets.DefaultImage [ post "a-post" (Some postImage) None ])
        (Error [ $"The post a-post has the Open Graph image {postImage}, and the build cannot make its crop: the file is not an image." ])
        "A file that is not an image fails"
    }

    test "the real posts give one crop for each distinct source" {
      let sources = realPosts.Value |> List.choose OgImages.source |> List.distinctBy _.AbsoluteUri
      let map = RenderMap.create Stage.Production widgets realContent.Value
      Expect.isNonEmpty sources "The posts have images"
      Expect.equal (ogFiles map).Count sources.Length "One crop for each source"
    }
  ]
