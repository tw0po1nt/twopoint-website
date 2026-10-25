namespace TwoPoint.Core.Posts

open TwoPoint.Site.Render

[<RequireQualifiedAccess>]
module PostCheck =

  type ParsePublished = StoredPost list -> StoredSeries list -> Series list * Post list

  let parsePublished (stage : Stage) : ParsePublished =
    fun posts records ->
      let series = StoredSeries.parse records

      series,
      posts
      |> List.choose (fun post -> Check.published stage series (Slug.value post.Slug) post.Text |> Result.toOption)

  /// `now` stands in for the missing publish date of a draft.
  let run
    (stage : Stage)
    (images : IImageSize)
    (now : System.DateTime)
    (isPublished : bool)
    (series : Series list, posts : Post list)
    (slug : Slug)
    (text : string)
    : Check.CheckedPost =
    if isPublished then
      Check.post stage images series posts (Slug.value slug) text
    else
      Check.draft stage images series (System.DateTimeOffset now) posts (Slug.value slug) text

  type Outcome =
    | Problems of Problem list
    | Unavailable
