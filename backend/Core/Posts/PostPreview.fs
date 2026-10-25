namespace TwoPoint.Core.Posts.Actions

open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

[<RequireQualifiedAccess>]
module PreviewPost =

  type Dto =
    { Slug : string
      Title : string
      Excerpt : string
      Body : string
      Details : TwoPoint.Site.Render.PostText.Details option }

  type Error =
    | PostNotFound of Slug
    | FrontMatterUnreadable of reason: string

  type Dependencies =
    abstract member Stage: TwoPoint.Site.Render.Stage
    abstract member Images: TwoPoint.Site.Render.IImageSize
    abstract member GetUtcNow: unit -> System.DateTime
    abstract member GetWorkingBlob: slug: Slug -> DependencyResult<StoredBlob option>
    abstract member GetPublishedPosts: unit -> DependencyResult<StoredPost list>
    abstract member GetSeriesRecords: unit -> DependencyResult<StoredSeries list>
    abstract member ParsePublished: PostCheck.ParsePublished

  type Action = Dto -> CancellableTask<ActionResult<TwoPoint.Site.Render.Check.CheckedPost, Error>>

  [<RequireQualifiedAccess>]
  module Logic =

    let baseText (slug : Slug) (working : StoredBlob option) (published : StoredPost list) : Decision<string, Error> =
      match working, published |> List.tryFind (fun post -> post.Slug = slug) with
      | Some blob, _ -> Decision.success blob.Text
      | None, Some post -> Decision.success post.Text
      | None, None -> Decision.failure (PostNotFound slug)

  let withDependencies (dependencies : Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! slug = dto.Slug |> Slug.create |> Result.mapError ActionError<Error>.Validation

      let! working, (published, records) =
        CancellableTaskResult.parallelZip
          (dependencies.GetWorkingBlob slug)
          (CancellableTaskResult.parallelZip (dependencies.GetPublishedPosts()) (dependencies.GetSeriesRecords()))
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! baseText = Logic.baseText slug working published |> Result.mapError ActionError<Error>.Logic

      let fields : TwoPoint.Site.Render.PostText.Fields = { Title = dto.Title; Excerpt = dto.Excerpt; Body = dto.Body }

      let composed =
        match dto.Details with
        | Some details -> TwoPoint.Site.Render.PostText.composeWithDetails baseText fields details
        | None -> TwoPoint.Site.Render.PostText.compose baseText fields

      let! text = composed |> Result.mapError (FrontMatterUnreadable >> ActionError<Error>.Logic)

      let kind =
        if published |> List.exists (fun post -> post.Slug = slug) then
          PostCheck.Published
        else
          PostCheck.Draft(dependencies.GetUtcNow())

      return
        PostCheck.forEditor
          dependencies.Stage
          dependencies.Images
          kind
          (dependencies.ParsePublished published records)
          slug
          text
    }
