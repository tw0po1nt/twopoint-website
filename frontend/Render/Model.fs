namespace TwoPoint.Site.Render

open System

[<RequireQualifiedAccess>]
type Severity =
  /// Makes the build fail.
  | Error
  /// The build prints it and does not fail.
  | Warning

[<RequireQualifiedAccess>]
type Place =
  /// A front matter field. `line` is the file line that the build prints.
  | Field of name: string * line: int
  /// Line 1 is the first line after the front matter.
  | Body of line: int
  /// The whole post. `line` is the file line that the build prints.
  | Post of line: int

type Problem =
  { Severity: Severity
    Place: Place
    Message: string }

[<RequireQualifiedAccess>]
module Problem =

  let error place message : Problem =
    { Severity = Severity.Error
      Place = place
      Message = message }

  let warning place message : Problem =
    { Severity = Severity.Warning
      Place = place
      Message = message }

  let isError (problem: Problem) = problem.Severity = Severity.Error

/// The problems of one file. `File` is the path from the repo root, and `BodyStart` is the file line of body line 1.
type FileProblems =
  { File: string
    BodyStart: int
    Problems: Problem list }

[<RequireQualifiedAccess>]
module FileProblems =

  let create file bodyStart problems : FileProblems =
    { File = file
      BodyStart = bodyStart
      Problems = problems }

  let line (problems: FileProblems) (place: Place) =
    match place with
    | Place.Field(_, line)
    | Place.Post line -> line
    | Place.Body line -> problems.BodyStart + line - 1

  let describePlace (problems: FileProblems) (place: Place) =
    match place with
    | Place.Body line -> $"body line {line}"
    | place -> $"line {line problems place}"

  /// One `file:line` line for each problem, in line order.
  let format (problems: FileProblems) =
    problems.Problems
    |> List.sortBy (fun problem -> line problems problem.Place)
    |> List.map (fun problem ->
      let severity =
        match problem.Severity with
        | Severity.Error -> ""
        | Severity.Warning -> "warning: "

      $"{problems.File}:{line problems problem.Place}: {severity}{problem.Message}")

  /// The lines of `format` for each file, in file order.
  let formatAll (problems: FileProblems list) =
    problems
    |> List.groupBy _.File
    |> List.sortBy fst
    |> List.collect (fun (_, group) -> format { List.head group with Problems = List.collect _.Problems group })

  let hasError (problems: FileProblems) = List.exists Problem.isError problems.Problems

  /// An `Ok` value with an error becomes an `Error`. An `Ok` value keeps its warnings.
  let ofResult file bodyStart (result: Result<'a * Problem list, Problem list>) : Result<'a * FileProblems, FileProblems> =
    match result with
    | Ok(value, problems) when not (List.exists Problem.isError problems) -> Ok(value, create file bodyStart problems)
    | Ok(_, problems)
    | Error problems -> Error(create file bodyStart problems)

  /// `Ok` when no file has an error. Each case has the files with a problem.
  let collect (results: Result<'a * FileProblems, FileProblems> list) : Result<'a list * FileProblems list, FileProblems list> =
    let problems =
      results
      |> List.map (function
        | Ok(_, problems)
        | Error problems -> problems)
      |> List.filter (fun problems -> not problems.Problems.IsEmpty)

    let failed =
      function
      | Ok(_, problems) -> hasError problems
      | Error _ -> true

    if List.exists failed results then
      Error problems
    else
      Ok(results |> List.choose Result.toOption |> List.map fst, problems)

/// A category or a tag.
type Term = { Slug: string; Title: string }

type Series =
  { Slug: string
    Title: string
    Excerpt: string
    Image: Uri option }

/// A term and its line in the post file.
type PlacedTerm = { Term: Term; Line: int }

type FrontMatter =
  { PublishDate: DateTimeOffset
    UpdateDate: DateTimeOffset option
    Title: string
    Excerpt: string
    Author: string
    Category: Term
    CategoryLine: int
    PlacedTags: PlacedTerm list
    Series: Series option
    Image: Uri option
    ShowOnHomepage: bool }

  member this.Tags = this.PlacedTags |> List.map _.Term

[<RequireQualifiedAccess>]
type PostState =
  /// A draft has no publish date, and it can have no category.
  | Draft
  | Published

type RuleFields =
  { PublishDate: DateTimeOffset option
    Category: Term option
    /// A missing category has the file line of the opening `---`.
    CategoryLine: int
    Tags: PlacedTerm list
    /// The slug of the series.
    Series: string option
    /// The file line of the `tags` field.
    TagsLine: int }

type LinkTarget =
  /// A root-relative link, for example `/posts/<slug>/#<id>`.
  | Page of path: string * anchor: string option
  /// A link to a heading of the same post, for example `#<id>`.
  | SamePage of anchor: string

/// `Line` is a body line.
type InternalLink = { Target: LinkTarget; Line: int }

type Heading = { Level: int; Id: string; Text: string }

type Body =
  { Html: string
    /// The body of the newsletter. The newsletter resolves its relative links against the post URL.
    EmailHtml: string
    Headings: Heading list
    Anchors: Set<string>
    Links: InternalLink list
    /// In minutes, at 200 words a minute, rounded up.
    ReadingTime: int
    /// The file line of body line 1.
    StartLine: int }

type Post =
  { File: string
    Slug: string
    FrontMatter: FrontMatter
    Body: Body }
