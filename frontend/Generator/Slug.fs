[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.Slug

open System
open System.Globalization
open System.IO
open System.Text
open System.Text.RegularExpressions

let private keepsInHeading (c: char) =
  match Char.GetUnicodeCategory c with
  | UnicodeCategory.UppercaseLetter
  | UnicodeCategory.LowercaseLetter
  | UnicodeCategory.TitlecaseLetter
  | UnicodeCategory.ModifierLetter
  | UnicodeCategory.OtherLetter
  | UnicodeCategory.NonSpacingMark
  | UnicodeCategory.SpacingCombiningMark
  | UnicodeCategory.EnclosingMark
  | UnicodeCategory.DecimalDigitNumber
  | UnicodeCategory.LetterNumber
  | UnicodeCategory.OtherNumber
  | UnicodeCategory.ConnectorPunctuation -> true
  | _ -> c = '-' || c = ' '

let private headingSlug (text: string) =
  String(text.ToLowerInvariant() |> Seq.filter keepsInHeading |> Array.ofSeq).Replace(' ', '-')

/// The `github-slugger` ids that the Astro build gives to the headings of one page.
let headingIds (texts: string list) =
  let next (occurrences: Map<string, int>, ids) text =
    let original = headingSlug text

    let rec claimUnusedId (occurrences: Map<string, int>) result =
      if occurrences.ContainsKey result then
        let count = occurrences[original] + 1
        claimUnusedId (occurrences.Add(original, count)) $"{original}-{count}"
      else
        occurrences.Add(result, 0), result

    let occurrences, id = claimUnusedId occurrences original
    occurrences, id :: ids

  texts |> List.fold next (Map.empty, []) |> snd |> List.rev

let private apostrophe = Regex(@"(\S)['‘’‚‛′‵](\S)")
let private separators = Regex("[^a-z0-9_]+")

let private withoutMarks (text: string) =
  text.Normalize(NormalizationForm.FormD)
  |> Seq.filter (fun c -> Char.GetUnicodeCategory c <> UnicodeCategory.NonSpacingMark)
  |> Array.ofSeq
  |> String

/// The `limax` slug that the Astro build gives to a category or a tag.
let term (text: string) =
  let joined = apostrophe.Replace(text, "$1$2").Replace("&", " and ")
  separators.Replace((withoutMarks joined).ToLowerInvariant(), "-").Trim '-'

let private languageNames =
  [ Regex("fsharp", RegexOptions.IgnoreCase), "F#"
    Regex("csharp", RegexOptions.IgnoreCase), "C#" ]

/// Gives the title of a category or a tag. For example, `fsharp` gives `F#`.
let termTitle (text: string) =
  languageNames |> List.fold (fun (title: string) (pattern, value) -> pattern.Replace(title, value)) text

/// `file` is the path from the repo root.
let ofFileName (file: string) : Result<string, PostError list> =
  let slug = Path.GetFileNameWithoutExtension file

  if term slug = slug then
    Ok slug
  else
    Error [ PostError.at file 1 $"Give the file a slug as its name, for example {term slug}{Path.GetExtension file}." ]
