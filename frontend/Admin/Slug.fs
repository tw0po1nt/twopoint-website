[<RequireQualifiedAccess>]
module TwoPoint.Admin.Slug

open System
open System.Text.RegularExpressions

/// The slug that the site gives to a category or a tag. It has the same rule as the build.
let term (text: string) =
  let joined = Regex.Replace(text, "(\\S)['‘’‚‛′‵](\\S)", "$1$2").Replace("&", " and ")
  let withoutMarks = Regex.Replace(joined.Normalize(Text.NormalizationForm.FormD), "[̀-ͯ]", "")
  Regex.Replace(withoutMarks.ToLowerInvariant(), "[^a-z0-9_]+", "-").Trim '-'

/// The title that the site shows for a category or a tag. For example, `FSharp` gives `F#`.
let termTitle (text: string) =
  Regex.Replace(Regex.Replace(text, "fsharp", "F#", RegexOptions.IgnoreCase), "csharp", "C#", RegexOptions.IgnoreCase)

let isSlug (text: string) = Regex.IsMatch(text, "^[a-z0-9]+(-[a-z0-9]+)*$")
