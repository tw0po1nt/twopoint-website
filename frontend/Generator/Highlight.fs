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

type private Entry =
  { Fence: string
    Language: Language
    Scope: string
    File: string }

let private entries =
  [ { Fence = "fsharp"; Language = Fsharp; Scope = "source.fsharp"; File = "fsharp.json" }
    { Fence = "csharp"; Language = Csharp; Scope = "source.cs"; File = "csharp.json" }
    { Fence = "swift"; Language = Swift; Scope = "source.swift"; File = "swift.json" }
    { Fence = "sh"; Language = Sh; Scope = "source.shell"; File = "shellscript.json" }
    { Fence = "diff"; Language = Diff; Scope = "source.diff"; File = "diff.json" }
    { Fence = "json"; Language = Json; Scope = "source.json"; File = "json.json" }
    { Fence = "xml"; Language = Xml; Scope = "text.xml"; File = "xml.json" }
    { Fence = "css"; Language = Css; Scope = "source.css"; File = "css.json" } ]

let names = entries |> List.map _.Fence |> String.concat ", "

let tryLanguage (fence: string) =
  entries |> List.tryFind (fun entry -> entry.Fence = fence) |> Option.map _.Language

let private scopeName language =
  (entries |> List.find (fun entry -> entry.Language = language)).Scope

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

let private themeRule (scope: JsonNode) paint =
  let settings = JsonObject()
  settings["foreground"] <- JsonValue.Create(color paint)
  let rule = JsonObject()
  rule["scope"] <- scope
  rule["settings"] <- settings
  rule :> JsonNode

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
         yield themeRule scope (paintOf foreground)
       for scope, paint in markScopes do
         yield themeRule (JsonValue.Create scope) paint |]

  let json = JsonObject()
  json["settings"] <- JsonArray rules
  use reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(json.ToJsonString())))
  ThemeReader.ReadThemeSync reader

let private registry =
  lazy
    (let files = entries |> List.map (fun entry -> entry.Scope, entry.File) |> dict
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

let plain (lines: string list) =
  lines |> List.map (fun line -> escape line + "\n") |> String.concat ""
