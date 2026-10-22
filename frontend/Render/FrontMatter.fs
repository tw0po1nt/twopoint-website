[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module TwoPoint.Site.Render.FrontMatter

open FsToolkit.ErrorHandling
open System
open System.Globalization
open System.IO
open YamlDotNet.Core
open YamlDotNet.RepresentationModel

type private Context = { StartLine: int; Document: string }

let private fileLine (context: Context) (yamlLine: int64) = context.StartLine + int yamlLine

let private fail context name (node: YamlNode) message =
  Error [ Problem.error (Place.Field(name, fileLine context node.Start.Line)) message ]

type private Parse<'a> = Context -> string -> YamlNode -> Result<'a, Problem list>

let private text: Parse<string> =
  fun context name node ->
    match node with
    | :? YamlScalarNode as scalar when not (String.IsNullOrWhiteSpace scalar.Value) -> Ok scalar.Value
    | _ -> fail context name node $"Give '{name}' as text."

let private flag: Parse<bool> =
  fun context name node ->
    match text context name node with
    | Ok "true" -> Ok true
    | Ok "false" -> Ok false
    | _ -> fail context name node $"Give '{name}' as true or false."

let private utcTime: Parse<DateTimeOffset> =
  fun context name node ->
    let parsed =
      match text context name node with
      | Ok value when value.EndsWith 'Z' ->
        match DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal) with
        | true, time -> Some time
        | false, _ -> None
      | _ -> None

    match parsed with
    | Some time -> Ok time
    | None -> fail context name node $"Give '{name}' as an ISO 8601 UTC time, for example 2026-02-04T13:00:00Z."

let private httpsUrl: Parse<Uri> =
  fun context name node ->
    match text context name node with
    | Ok value ->
      match Uri.TryCreate(value, UriKind.Absolute) with
      | true, url when url.Scheme = Uri.UriSchemeHttps -> Ok url
      | _ -> fail context name node $"Give '{name}' as an https URL."
    | Error errors -> Error errors

let private term: Parse<Term> =
  fun context name node ->
    text context name node
    |> Result.bind (fun value ->
      match Slug.term value with
      | "" -> fail context name node $"'{name}' gives an empty slug."
      | slug -> Ok { Slug = slug; Title = Slug.termTitle value })

let private terms: Parse<Term list> =
  fun context name node ->
    match node with
    | :? YamlSequenceNode as sequence ->
      sequence.Children
      |> List.ofSeq
      |> List.traverseResultA (term context name)
      |> Result.mapError List.concat
    | _ -> fail context name node $"Give '{name}' as a list."

let private mapping context (prefix: string) (node: YamlNode) (known: string list) =
  match node with
  | :? YamlMappingNode as mapping ->
    let fields =
      mapping.Children
      |> Seq.choose (fun (KeyValue(key, value)) ->
        match key with
        | :? YamlScalarNode as scalar -> Some(scalar.Value, (key, value))
        | _ -> None)
      |> Map.ofSeq

    let unknown =
      fields
      |> Map.toList
      |> List.filter (fun (field, _) -> not (List.contains field known))
      |> List.map (fun (field, (key, _)) ->
        Problem.error (Place.Field($"{prefix}{field}", fileLine context key.Start.Line)) $"Unknown field '{prefix}{field}'.")

    if unknown.IsEmpty then
      Ok(fields |> Map.map (fun _ (_, value) -> value))
    else
      Error unknown
  | _ when prefix = "" ->
    Error [ Problem.error (Place.Post(fileLine context node.Start.Line)) $"Give the {context.Document} as a YAML mapping." ]
  | _ -> fail context (prefix.TrimEnd '.') node $"Give '{prefix.TrimEnd '.'}' as a mapping."

let private required context (missingLine: int64) (fields: Map<string, YamlNode>) prefix name (parse: Parse<'a>) =
  match fields.TryFind name with
  | Some node -> parse context $"{prefix}{name}" node
  | None ->
    Error
      [ Problem.error
          (Place.Field($"{prefix}{name}", fileLine context missingLine))
          $"The {context.Document} has no '{prefix}{name}' field." ]

let private optional context (fields: Map<string, YamlNode>) prefix name (parse: Parse<'a>) =
  match fields.TryFind name with
  | Some node -> parse context $"{prefix}{name}" node |> Result.map Some
  | None -> Ok None

let private seriesBySlug (records: Series list) : Parse<Series> =
  fun context name node ->
    text context name node
    |> Result.bind (fun slug ->
      match records |> List.tryFind (fun record -> record.Slug = slug) with
      | Some record -> Ok record
      | None -> fail context name node $"No series has the slug '{slug}'.")

let private fieldNames =
  [ "publishDate"
    "updateDate"
    "title"
    "excerpt"
    "author"
    "category"
    "tags"
    "series"
    "image"
    "showOnHomepage" ]

let private ofRoot (records: Series list) context (root: YamlNode) =
  result {
    let! fields = mapping context "" root fieldNames
    let required name parse = required context 0L fields "" name parse
    let optional name parse = optional context fields "" name parse

    return!
      validation {
        let! publishDate = required "publishDate" utcTime
        and! updateDate = optional "updateDate" utcTime
        and! title = required "title" text
        and! excerpt = required "excerpt" text
        and! author = required "author" text
        and! category = required "category" term
        and! tags = optional "tags" terms
        and! series = optional "series" (seriesBySlug records)
        and! image = optional "image" httpsUrl
        and! showOnHomepage = optional "showOnHomepage" flag

        return
          { PublishDate = publishDate
            UpdateDate = updateDate
            Title = title
            Excerpt = excerpt
            Author = author
            Category = category
            Tags = defaultArg tags []
            Series = series
            Image = image
            ShowOnHomepage = defaultArg showOnHomepage true }
      }
  }

let private load (document: string) (startLine: int) (yaml: string) (ofRoot: Context -> YamlNode -> Result<'a, Problem list>) =
  let context = { StartLine = startLine; Document = document }

  try
    let stream = YamlStream()
    stream.Load(new StringReader(yaml))

    match List.ofSeq stream.Documents with
    | [ document ] -> ofRoot context document.RootNode
    | _ -> Error [ Problem.error (Place.Post(fileLine context 0L)) $"The {document} is not one YAML mapping." ]
  with :? YamlException as error ->
    Error [ Problem.error (Place.Post(fileLine context error.Start.Line)) error.Message ]

/// `startLine` is the file line of the opening `---`.
let parse (records: Series list) (startLine: int) (yaml: string) : Result<FrontMatter, Problem list> =
  load "front matter" startLine yaml (ofRoot records)

let private ofPageRoot context (root: YamlNode) =
  result {
    let! fields = mapping context "" root [ "title"; "description" ]
    let required name parse = required context 0L fields "" name parse

    return!
      validation {
        let! title = required "title" text
        and! description = required "description" text
        return title, description
      }
  }

/// The title and the description of a page that is not a post. `startLine` is the file line of the opening `---`.
let parsePage (startLine: int) (yaml: string) : Result<string * string, Problem list> =
  load "front matter" startLine yaml ofPageRoot

let private ofSeriesRoot (slug: string) context (root: YamlNode) =
  result {
    let! fields = mapping context "" root [ "title"; "excerpt"; "image" ]
    let required name parse = required context 1L fields "" name parse

    return!
      validation {
        let! title = required "title" text
        and! excerpt = required "excerpt" text
        and! image = optional context fields "" "image" httpsUrl

        return
          { Slug = slug
            Title = title
            Excerpt = excerpt
            Image = image }
      }
  }

/// A series record is a YAML file, and its file name gives the slug.
let parseSeries (file: string) (yaml: string) : Result<Series, FileProblems> =
  Slug.ofFileName file
  |> Result.bind (fun slug -> load "series record" 0 yaml (ofSeriesRoot slug))
  |> Result.mapError (FileProblems.create file 1)
