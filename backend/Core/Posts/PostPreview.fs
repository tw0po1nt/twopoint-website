namespace TwoPoint.Core.Posts.Actions

open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

[<RequireQualifiedAccess>]
module PreviewPost =

  /// The fields are the text in the editor, saved or not.
  type Dto =
    { Slug : string
      Title : string
      Excerpt : string
      Body : string }

  type Error =
    | PostNotFound of Slug
    /// The YAML parser cannot read the front matter of the stored text.
    | FrontMatterUnreadable of reason: string

  type Dependencies =
    abstract member Stage: TwoPoint.Site.Render.Stage
    abstract member Images: TwoPoint.Site.Render.IImageSize
    abstract member GetUtcNow: unit -> System.DateTime
    abstract member GetWorkingBlob: slug: Slug -> DependencyResult<StoredBlob option>
    abstract member GetPublishedPosts: unit -> DependencyResult<StoredPost list>
    abstract member GetSeriesRecords: unit -> DependencyResult<StoredSeries list>

  type Action = Dto -> CancellableTask<ActionResult<TwoPoint.Site.Render.Check.CheckedPost, Error>>

  [<RequireQualifiedAccess>]
  module Logic =

    /// The working blob wins over the published blob, as in the editor.
    let baseText (slug : Slug) (working : StoredBlob option) (published : StoredPost list) : Decision<string, Error> =
      match working, published |> List.tryFind (fun post -> post.Slug = slug) with
      | Some blob, _ -> Decision.success blob.Text
      | None, Some post -> Decision.success post.Text
      | None, None -> Decision.failure (PostNotFound slug)

    /// A post that does not parse is left out. The next build reports it.
    let publishedPosts (stage : TwoPoint.Site.Render.Stage) (series : TwoPoint.Site.Render.Series list) (posts : StoredPost list) : TwoPoint.Site.Render.Post list =
      posts
      |> List.choose (fun post -> TwoPoint.Site.Render.Check.published stage series (Slug.value post.Slug) post.Text |> Result.toOption)

  let withDependencies (dependencies : Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! slug = dto.Slug |> Slug.create |> Result.mapError ActionError<Error>.Validation

      let! working, (published, records) =
        CancellableTaskResult.parallelZip
          (dependencies.GetWorkingBlob slug)
          (CancellableTaskResult.parallelZip (dependencies.GetPublishedPosts()) (dependencies.GetSeriesRecords()))
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! baseText = Logic.baseText slug working published |> Result.mapError ActionError<Error>.Logic

      let! text =
        TwoPoint.Site.Render.PostText.compose baseText { Title = dto.Title; Excerpt = dto.Excerpt; Body = dto.Body }
        |> Result.mapError (FrontMatterUnreadable >> ActionError<Error>.Logic)

      let series = StoredSeries.parse records
      let posts = Logic.publishedPosts dependencies.Stage series published

      let isPublished = published |> List.exists (fun post -> post.Slug = slug)

      // A post with no published blob is a draft. The check shows the time of the preview as its publish date.
      return
        if isPublished then
          TwoPoint.Site.Render.Check.post dependencies.Stage dependencies.Images series posts dto.Slug text
        else
          TwoPoint.Site.Render.Check.draft
            dependencies.Stage
            dependencies.Images
            series
            (System.DateTimeOffset(dependencies.GetUtcNow()))
            posts
            dto.Slug
            text
    }
