/// Gives the colors of a code block at build time.
[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.Highlight

open System
open System.IO
open System.Reflection
open System.Text
open System.Text.Json.Nodes
open TextMateSharp.Grammars
open TextMateSharp.Internal.Grammars
open TextMateSharp.Internal.Grammars.Reader
open TextMateSharp.Internal.Themes.Reader
open TextMateSharp.Registry

type Language =
  | Fsharp
  | Csharp
  | Swift
  | Sh
  | Diff
  | Json
  | Xml
  | Css

/// Each fence language, by the name that the fence gives.
let languages =
  [ "fsharp", Fsharp
    "csharp", Csharp
    "swift", Swift
    "sh", Sh
    "diff", Diff
    "json", Json
    "xml", Xml
    "css", Css ]

let tryLanguage (fence: string) =
  languages |> List.tryFind (fun (name, _) -> name = fence) |> Option.map snd

let private grammarFile =
  function
  | Fsharp -> "fsharp.json"
  | Csharp -> "csharp.json"
  | Swift -> "swift.json"
  | Sh -> "shellscript.json"
  | Diff -> "diff.json"
  | Json -> "json.json"
  | Xml -> "xml.json"
  | Css -> "css.json"

let private scopeName =
  function
  | Fsharp -> "source.fsharp"
  | Csharp -> "source.cs"
  | Swift -> "source.swift"
  | Sh -> "source.shell"
  | Diff -> "source.diff"
  | Json -> "source.json"
  | Xml -> "text.xml"
  | Css -> "source.css"

/// The closed set of roles of the scope map. The name of a role is the end of its variable name in the map.
let private roles =
  [ "keyword"
    "string"
    "string-expression"
    "comment"
    "function"
    "constant"
    "parameter"
    "punctuation"
    "link"
    "inserted"
    "deleted"
    "changed" ]

type private Paint =
  | Plain
  | Role of string
  /// The first character of a changed diff line, for example `+`, with the role of its line.
  | Mark of string

/// TextMateSharp matches each scope to a color, so each paint gets a color that is its index in this list.
let private paints =
  [ yield Plain
    for role in roles -> Role role
    yield Mark "inserted"
    yield Mark "deleted" ]
  |> Array.ofList

let private color (paint: Paint) = $"#%06x{Array.IndexOf(paints, paint)}"

let private markScopes =
  [ "punctuation.definition.inserted.diff", Mark "inserted"
    "punctuation.definition.deleted.diff", Mark "deleted" ]

let private resource (name: string) =
  Assembly.GetExecutingAssembly().GetManifestResourceStream $"Grammars/{name}"

let private paintOf (variable: string) =
  match variable with
  | "var(--shiki-foreground)" -> Plain
  | _ ->
    let role = variable.Replace("var(--shiki-token-", "").TrimEnd(')')

    if List.contains role roles then
      Role role
    else
      failwith $"The scope map has the unknown color '{variable}'."

let private rule (scope: JsonNode) paint =
  let settings = JsonObject()
  settings["foreground"] <- JsonValue.Create(color paint)
  let rule = JsonObject()
  rule["scope"] <- scope
  rule["settings"] <- settings
  rule :> JsonNode

/// The scope map as a TextMate theme.
let private theme () =
  use stream = resource "css-variables.json"
  let map = JsonNode.Parse stream
  let defaults = JsonObject()
  defaults["settings"] <- JsonNode.Parse $"""{{ "foreground": "{color Plain}", "background": "#ffffff" }}"""

  let rules =
    [| yield defaults :> JsonNode
       for entry in map["tokenColors"].AsArray() do
         let settings = entry["settings"]
         let foreground = settings["foreground"].GetValue<string>()
         let scope = entry["scope"].DeepClone()
         yield rule scope (paintOf foreground)
       for scope, paint in markScopes do
         yield rule (JsonValue.Create scope) paint |]

  let json = JsonObject()
  json["settings"] <- JsonArray rules
  use reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(json.ToJsonString())))
  ThemeReader.ReadThemeSync reader

let private registry =
  lazy
    (let files = languages |> List.map (fun (_, language) -> scopeName language, grammarFile language) |> dict
     let theme = theme ()

     Registry(
       { new IRegistryOptions with
           member _.GetGrammar scope =
             match files.TryGetValue scope with
             | true, file ->
               use reader = new StreamReader(resource file)
               GrammarReader.ReadGrammarSync reader
             // The F# grammar embeds Markdown in a doc comment, and the XML grammar embeds Java. The site has no grammar for them.
             | false, _ -> null

           member _.GetInjections _ = null
           member _.GetTheme _ = theme
           member _.GetDefaultTheme() = theme }
     ))

let private grammars = Collections.Generic.Dictionary<Language, IGrammar>()

// TextMateSharp is not safe to use from two threads.
let private gate = obj ()

let private escape (text: string) =
  text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;")

let private roleOf =
  function
  | Plain -> None
  | Role role
  | Mark role -> Some role

let private segmentHtml (paint, text: string) =
  match paint with
  | Mark _ when text = "+" || text = "-" -> $"<span class=\"tk-diff-mark\">{text}</span>"
  | _ -> escape text

let private lineHtml (segments: (Paint * string) list) =
  segments
  |> List.fold
    (fun runs (paint, text) ->
      match runs with
      | (role, run) :: rest when role = roleOf paint -> (role, (paint, text) :: run) :: rest
      | _ -> (roleOf paint, [ paint, text ]) :: runs)
    []
  |> List.rev
  |> List.map (fun (role, run) ->
    let inner = run |> List.rev |> List.map segmentHtml |> String.concat ""

    match role with
    | Some role -> $"<span class=\"tk-{role}\">{inner}</span>"
    | None -> inner)
  |> String.concat ""

/// Gives the HTML of the `code` element, with each line and its newline.
let html (language: Language) (lines: string list) =
  let output = StringBuilder()

  lock gate (fun () ->
    let registry = registry.Value

    if not (grammars.ContainsKey language) then
      grammars[language] <- registry.LoadGrammar(scopeName language)

    let grammar = grammars[language]
    let theme = registry.GetTheme()

    lines
    |> List.fold
      (fun (state: IStateStack) line ->
        let result = grammar.TokenizeLine2(LineText line, state, TimeSpan.MaxValue)
        let tokens = result.Tokens

        let segments =
          [ for index in 0..2 .. tokens.Length - 1 do
              let start = tokens[index]
              let finish = if index + 2 < tokens.Length then tokens[index + 2] else line.Length

              if start < line.Length then
                let id = Convert.ToInt32(theme.GetColor(EncodedTokenAttributes.GetForeground tokens[index + 1]).TrimStart '#', 16)
                yield paints[id], line.Substring(start, min finish line.Length - start) ]

        output.Append(lineHtml segments).Append('\n') |> ignore
        result.RuleStack)
      null
    |> ignore)

  output.ToString()

/// Gives the HTML of the `code` element of a block with no language.
let plain (lines: string list) =
  lines |> List.map (fun line -> escape line + "\n") |> String.concat ""
