namespace TwoPoint.Core.Posts

open TwoPoint.Core.Util
open TwoPoint.Site.Render

[<RequireQualifiedAccess>]
module PostCheck =

  type ParsePublished = StoredPost list -> StoredSeries list -> Series list * Post list

  let parsePost (stage : Stage) (series : Series list) (post : StoredPost) : Post option =
    Check.published stage series (Slug.value post.Slug) post.Text |> Result.toOption

  let parsePublished (stage : Stage) : ParsePublished =
    fun posts records ->
      let series = StoredSeries.parse records
      series, posts |> List.choose (parsePost stage series)

  type Kind =
    | Published
    | Draft of previewTime : System.DateTime

  let forEditor
    (stage : Stage)
    (images : IImageSize)
    (kind : Kind)
    (series : Series list, posts : Post list)
    (slug : Slug)
    (text : string)
    : Check.CheckedPost =
    match kind with
    | Published -> Check.post stage images series posts (Slug.value slug) text
    | Draft previewTime ->
      Check.draft stage images series (System.DateTimeOffset previewTime) posts (Slug.value slug) text

  type CheckFailure =
    | StoreUnavailable of DependencyError
    | CheckCrashed of exn

  type Outcome =
    | Problems of Problem list
    | Unavailable of CheckFailure
