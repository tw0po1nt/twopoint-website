[<RequireQualifiedAccess>]
module TwoPoint.Site.Render.Rules

open System.Text.RegularExpressions

let seriesCategory = "series"

let private categoryProblems (state: PostState) (fields: RuleFields) =
  let at = Place.Field("category", fields.CategoryLine)
  let isSeries = fields.Category |> Option.exists (fun category -> category.Slug = seriesCategory)

  match fields.Category, fields.Series with
  | _, Some _ when not isSeries -> [ Problem.error at "The post is in a series, so give it the category 'Series'." ]
  | Some _, None when isSeries ->
    [ Problem.error at "The category is 'Series', so give the post a series, or give it another category." ]
  | None, None when state = PostState.Published -> [ Problem.error at "The post has no category." ]
  | _ -> []

let private duplicateTags (fields: RuleFields) =
  fields.Tags
  |> List.indexed
  |> List.choose (fun (index, tag) ->
    if fields.Tags |> List.take index |> List.exists (fun earlier -> earlier.Term.Slug = tag.Term.Slug) then
      Some(
        Problem.error
          (Place.Field("tags", tag.Line))
          $"Another tag has the slug '{tag.Term.Slug}'. Delete one of the two tags."
      )
    else
      None)

let private yearTag = Regex(@"^\d{4}$")

let private yearProblems (fields: RuleFields) =
  match fields.PublishDate, fields.Tags with
  | Some publishDate, _ :: _ ->
    let year = string publishDate.UtcDateTime.Year

    let missing =
      if fields.Tags |> List.exists (fun tag -> tag.Term.Slug = year) then
        []
      else
        [ Problem.warning (Place.Field("tags", fields.TagsLine)) $"Add the tag '{year}' for the publish year." ]

    let otherYears =
      fields.Tags
      |> List.filter (fun tag -> yearTag.IsMatch tag.Term.Slug && tag.Term.Slug <> year)
      |> List.map (fun tag ->
        Problem.warning
          (Place.Field("tags", tag.Line))
          $"The tag '{tag.Term.Slug}' is a year tag, and the publish year is {year}. Delete the tag or change it to '{year}'.")

    missing @ otherYears
  | _ -> []

/// A post with no tags gets no tag problem.
let frontMatter (state: PostState) (fields: RuleFields) =
  categoryProblems state fields @ duplicateTags fields @ yearProblems fields

let private termFields (post: Post) =
  let frontMatter = post.FrontMatter

  [ "category", "category", [ { Term = frontMatter.Category; Line = frontMatter.CategoryLine } ], (fun (other: Post) -> [ other.FrontMatter.Category ])
    "tags", "tag", frontMatter.PlacedTags, (fun (other: Post) -> other.FrontMatter.Tags) ]

/// A warning for each term of `post` that another post of `published` gives another title.
let termTitles (published: Post list) (post: Post) =
  let others = published |> List.filter (fun other -> other.Slug <> post.Slug) |> List.sortBy _.Slug

  [ for field, name, terms, termsOf in termFields post do
      for { Term = term; Line = line } in terms do
        let otherTitles =
          others
          |> List.choose (fun other ->
            termsOf other
            |> List.tryFind (fun otherTerm -> otherTerm.Slug = term.Slug && otherTerm.Title <> term.Title)
            |> Option.map (fun otherTerm -> $"{other.Slug} ('{otherTerm.Title}')"))

        if not otherTitles.IsEmpty then
          let posts = String.concat ", " otherTitles

          Problem.warning
            (Place.Field(field, line))
            $"The {name} '{term.Title}' has the slug '{term.Slug}', and other posts give that slug another title: {posts}." ]
