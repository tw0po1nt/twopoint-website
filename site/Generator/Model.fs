namespace TwoPoint.Site.Generator

open System

/// `File` is the path from the repo root, and `Line` counts from 1.
type PostError =
  { File: string
    Line: int
    Message: string }

[<RequireQualifiedAccess>]
module PostError =

  let at file line message : PostError =
    { File = file
      Line = line
      Message = message }

  let format (error: PostError) = $"{error.File}:{error.Line}: {error.Message}"

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
    Draft: bool
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

type InternalLink = { Target: LinkTarget; Line: int }

type Heading = { Level: int; Id: string; Text: string }

type Body =
  { Html: string
    Headings: Heading list
    Anchors: Set<string>
    Links: InternalLink list
    /// In minutes, at 200 words a minute, rounded up.
    ReadingTime: int }

type Post =
  { File: string
    Slug: string
    FrontMatter: FrontMatter
    Body: Body }

type ParsedPost =
  | Draft
  | Published of Post
