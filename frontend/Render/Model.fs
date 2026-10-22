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

  /// The `Ok` value has the problems of the file that are not errors.
  let ofResult file bodyStart (result: Result<'a, Problem list>) : Result<'a * FileProblems, FileProblems> =
    match result with
    | Ok value -> Ok(value, create file bodyStart [])
    | Error problems -> Error(create file bodyStart problems)

  /// `Ok` when no file has an error. Each case has the files with a problem.
  let collect (results: Result<'a * FileProblems, FileProblems> list) : Result<'a list * FileProblems list, FileProblems list> =
    let problems =
      results
      |> List.map (function
        | Ok(_, problems)
        | Error problems -> problems)
      |> List.filter (fun problems -> not problems.Problems.IsEmpty)

    if List.exists Result.isError results then
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

type FrontMatter =
  { PublishDate: DateTimeOffset
    UpdateDate: DateTimeOffset option
    Title: string
    Excerpt: string
    Author: string
    Category: Term
    Tags: Term list
    Series: Series option
    Image: Uri option
    ShowOnHomepage: bool }

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
