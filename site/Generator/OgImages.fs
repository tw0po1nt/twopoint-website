namespace TwoPoint.Site.Generator

open FsToolkit.ErrorHandling

type OgImages =
  { /// Each crop at its site path, for example `_/og-1a2b3c4d.jpg`.
    Files: Map<string, byte array>
    /// By post slug. Each post has one.
    ByPost: Map<string, SizedImage> }

[<RequireQualifiedAccess>]
module OgImages =

  let source (post: Post) =
    post.FrontMatter.Series
    |> Option.bind _.Image
    |> Option.orElse post.FrontMatter.Image

  let private crop (images: IImages) url =
    images.OgCrop url
    |> Result.map (fun (bytes, size) ->
      let path = Assets.sitePath "og.jpg" bytes
      path, bytes, { UrlPath = $"/{path}"; Size = size })

  /// `defaultImage` is the image of a post with no series image and no post image.
  let create (images: IImages) (defaultImage: SizedImage) (posts: Post list) : Result<OgImages, string list> =
    let crops =
      posts
      |> List.choose source
      |> List.distinctBy _.AbsoluteUri
      |> List.map (fun url -> url.AbsoluteUri, crop images url)
      |> Map.ofList

    posts
    |> List.traverseResultA (fun post ->
      match source post with
      | None -> Ok(post.Slug, defaultImage, None)
      | Some url ->
        match crops[url.AbsoluteUri] with
        | Ok(path, bytes, image) -> Ok(post.Slug, image, Some(path, bytes))
        | Error reason ->
          Error $"The post {post.Slug} has the Open Graph image {url}, and the build cannot make its crop: {reason}.")
    |> Result.map (fun images ->
      { Files = images |> List.choose (fun (_, _, file) -> file) |> Map.ofList
        ByPost = images |> List.map (fun (slug, image, _) -> slug, image) |> Map.ofList })
