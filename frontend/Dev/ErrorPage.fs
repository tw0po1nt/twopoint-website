[<RequireQualifiedAccess>]
module TwoPoint.Site.Dev.ErrorPage

open Oxpecker.ViewEngine
open System.Text.RegularExpressions

/// The form of a post error: `frontend/content/posts/published/a-post.md:12: The message.`
let private location =
  Regex("^(?<file>[^\\s:]+):(?<line>\\d+): (?<message>.*)$", RegexOptions.Singleline)

let private item (error: string) =
  let found = location.Match error

  li () {
    if found.Success then
      code () { $"""{found.Groups["file"].Value}:{found.Groups["line"].Value}""" }
      " "
      found.Groups["message"].Value
    else
      error
  }

let render (errors: string list) : byte array =
  html (lang = "en") {
    head () {
      meta (charset = "utf-8")
      title () { "The site has errors" }
    }

    body () {
      h1 () { "The site has errors" }
      p () { "Fix each error and save. The page then reloads." }
      ol () {
        for error in errors do
          item error
      }
    }
  }
  |> Render.toHtmlDocBytes
