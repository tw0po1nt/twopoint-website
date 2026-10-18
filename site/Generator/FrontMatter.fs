[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module TwoPoint.Site.Generator.FrontMatter

open FsToolkit.ErrorHandling
open System
open System.Globalization
open System.IO
open YamlDotNet.Core
open YamlDotNet.RepresentationModel

type private Context = { File: string; StartLine: int }

let private errorAt (context: Context) (yamlLine: int64) message : PostError =
  { File = context.File
    Line = context.StartLine + int yamlLine
    Message = message }

let private fail context (node: YamlNode) message = Error [ errorAt context node.Start.Line message ]

type private Parse<'a> = Context -> string -> YamlNode -> Result<'a, PostError list>

let private text: Parse<string> =
  fun context name node ->
    match node with
    | :? YamlScalarNode as scalar when not (String.IsNullOrWhiteSpace scalar.Value) -> Ok scalar.Value
    | _ -> fail context node $"Give '{name}' as text."

let private flag: Parse<bool> =
  fun context name node ->
    match text context name node with
    | Ok "true" -> Ok true
    | Ok "false" -> Ok false
    | _ -> fail context node $"Give '{name}' as true or false."

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
    | None -> fail context node $"Give '{name}' as an ISO 8601 UTC time, for example 2026-02-04T13:00:00Z."

let private httpsUrl: Parse<Uri> =
  fun context name node ->
    match text context name node with
    | Ok value ->
      match Uri.TryCreate(value, UriKind.Absolute) with
      | true, url when url.Scheme = Uri.UriSchemeHttps -> Ok url
      | _ -> fail context node $"Give '{name}' as an https URL."
    | Error errors -> Error errors

let private term: Parse<Term> =
  fun context name node ->
    text context name node
    |> Result.bind (fun value ->
      match Slug.term value with
      | "" -> fail context node $"'{name}' gives an empty slug."
      | slug -> Ok { Slug = slug; Title = Slug.termTitle value })

let private terms: Parse<Term list> =
  fun context name node ->
    match node with
    | :? YamlSequenceNode as sequence ->
      sequence.Children
      |> List.ofSeq
      |> List.traverseResultA (term context name)
      |> Result.mapError List.concat
    | _ -> fail context node $"Give '{name}' as a list."

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
      |> List.map (fun (field, (key, _)) -> errorAt context key.Start.Line $"Unknown field '{prefix}{field}'.")

    if unknown.IsEmpty then
      Ok(fields |> Map.map (fun _ (_, value) -> value))
    else
      Error unknown
  | _ when prefix = "" -> fail context node "Give the front matter as a YAML mapping."
  | _ -> fail context node $"Give '{prefix.TrimEnd '.'}' as a mapping."

let private required context (missingLine: int64) (fields: Map<string, YamlNode>) prefix name (parse: Parse<'a>) =
  match fields.TryFind name with
  | Some node -> parse context $"{prefix}{name}" node
  | None -> Error [ errorAt context missingLine $"The front matter has no '{prefix}{name}' field." ]

let private optional context (fields: Map<string, YamlNode>) prefix name (parse: Parse<'a>) =
  match fields.TryFind name with
  | Some node -> parse context $"{prefix}{name}" node |> Result.map Some
  | None -> Ok None

let private series: Parse<Series> =
  fun context name node ->
    result {
      let! fields = mapping context $"{name}." node [ "slug"; "title"; "excerpt"; "image" ]
      let required name parse = required context node.Start.Line fields "series." name parse

      return!
        validation {
          let! slug = required "slug" text
          and! title = required "title" text
          and! excerpt = required "excerpt" text
          and! image = optional context fields "series." "image" httpsUrl

          return
            { Slug = slug
              Title = title
              Excerpt = excerpt
              Image = image }
        }
        |> Result.bind (fun (series: Series) ->
          if Slug.term series.Slug = series.Slug then
            Ok series
          else
            fail context fields["slug"] $"Give 'series.slug' as a slug, for example {Slug.term series.Slug}.")
    }

let private fieldNames =
  [ "publishDate"
    "updateDate"
    "draft"
    "title"
    "excerpt"
    "author"
    "category"
    "tags"
    "series"
    "image"
    "showOnHomepage" ]

let private ofRoot context (root: YamlNode) =
  result {
    let! fields = mapping context "" root fieldNames
    let required name parse = required context 0L fields "" name parse
    let optional name parse = optional context fields "" name parse

    return!
      validation {
        let! publishDate = required "publishDate" utcTime
        and! updateDate = optional "updateDate" utcTime
        and! draft = optional "draft" flag
        and! title = required "title" text
        and! excerpt = required "excerpt" text
        and! author = required "author" text
        and! category = required "category" term
        and! tags = optional "tags" terms
        and! series = optional "series" series
        and! image = optional "image" httpsUrl
        and! showOnHomepage = optional "showOnHomepage" flag

        return
          { PublishDate = publishDate
            UpdateDate = updateDate
            Draft = defaultArg draft false
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

/// `startLine` is the file line of the opening `---`.
let parse (file: string) (startLine: int) (yaml: string) : Result<FrontMatter, PostError list> =
  let context = { File = file; StartLine = startLine }

  try
    let stream = YamlStream()
    stream.Load(new StringReader(yaml))

    match List.ofSeq stream.Documents with
    | [ document ] -> ofRoot context document.RootNode |> Result.mapError (List.sortBy _.Line)
    | _ -> Error [ errorAt context 0L "The front matter is not one YAML mapping." ]
  with :? YamlException as error ->
    Error [ errorAt context error.Start.Line error.Message ]
