[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.Slug

open System
open System.Globalization
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

/// Gives the ids of the headings of one page, in order, with the `github-slugger` rules that the
/// Astro build uses. A repeated id gets the suffix `-1`, `-2`, and so on.
let headingIds (texts: string list) =
  let next (occurrences: Map<string, int>, ids) text =
    let original = headingSlug text

    let rec free (occurrences: Map<string, int>) result =
      if occurrences.ContainsKey result then
        let count = occurrences[original] + 1
        free (occurrences.Add(original, count)) $"{original}-{count}"
      else
        occurrences.Add(result, 0), result

    let occurrences, id = free occurrences original
    occurrences, id :: ids

  texts |> List.fold next (Map.empty, []) |> snd |> List.rev

let private apostrophe = Regex(@"(\S)['‘’‚‛′‵](\S)")
let private separators = Regex("[^a-z0-9_]+")

let private withoutMarks (text: string) =
  text.Normalize(NormalizationForm.FormD)
  |> Seq.filter (fun c -> Char.GetUnicodeCategory c <> UnicodeCategory.NonSpacingMark)
  |> Array.ofSeq
  |> String

/// Gives the slug of a category or a tag with the `limax` rules, as the Astro build does.
/// For example, `.NET Conf` gives `net-conf`.
let term (text: string) =
  let joined = apostrophe.Replace(text, "$1$2").Replace("&", " and ")
  separators.Replace((withoutMarks joined).ToLowerInvariant(), "-").Trim '-'

let private oddballs =
  [ Regex("fsharp", RegexOptions.IgnoreCase), "F#"
    Regex("csharp", RegexOptions.IgnoreCase), "C#" ]

/// Gives the title of a category or a tag. For example, `fsharp` gives `F#`.
let termTitle (text: string) =
  oddballs |> List.fold (fun (title: string) (pattern, value) -> pattern.Replace(title, value)) text
