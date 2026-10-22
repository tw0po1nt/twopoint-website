[<RequireQualifiedAccess>]
module TwoPoint.Site.Render.Article

open Oxpecker.ViewEngine

let private row (term: string) (definition: HtmlElement) =
  div () {
    dt () { term }
    dd () { definition }
  }

let private frontMatterBlock (post: Post) (position: Blog.SeriesPosition option) =
  let frontMatter = post.FrontMatter

  dl (class' = "front-matter") {
    row "date" (time (datetime = Time.isoTime frontMatter.PublishDate) { Time.shownDate frontMatter.PublishDate })

    match frontMatter.UpdateDate with
    | Some updated -> row "updated" (time (datetime = Time.isoTime updated) { Time.shownDate updated })
    | None -> ()

    row "author" (Fragment() { frontMatter.Author })

    match position with
    | Some position ->
      row
        "series"
        (Fragment() {
          a (href = $"/series/{position.Series.Slug}/") { position.Series.Slug }
          $" ({position.Position} of {position.Count})"
        })
    | None -> ()

    match frontMatter.Tags with
    | [] -> ()
    | tags ->
      row
        "tags"
        (Fragment() {
          for index, tag in List.indexed tags do
            if index > 0 then
              " "

            a (href = $"/tag/{tag.Slug}/") { $"#{tag.Title}" }
        })

    if post.Body.ReadingTime > 0 then
      row "read" (Fragment() { $"{post.Body.ReadingTime} min" })
  }

let content (post: Post) (position: Blog.SeriesPosition option) =
  Fragment() {
    frontMatterBlock post position
    h1 () { post.FrontMatter.Title }
    p (class' = "excerpt") { post.FrontMatter.Excerpt }
    div (class' = "prose") { raw post.Body.Html }
  }

let outline (post: Post) =
  let headings = post.Body.Headings |> List.filter (fun heading -> heading.Level = 2 || heading.Level = 4)

  aside(class' = "outline").attr ("aria-label", "Outline") {
    match headings with
    | [] -> ()
    | headings ->
      p (class' = "label") { "outline" }

      ul () {
        for heading in headings do
          li (class' = $"outline-h{heading.Level}") { a (href = $"#{heading.Id}") { heading.Text } }
      }
  }
