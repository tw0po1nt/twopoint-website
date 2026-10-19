/// Each class in the output has a CSS rule, and each CSS rule for a class has a use.
module TwoPoint.Site.Generator.Tests.ClassCheck

open System
open System.Text.RegularExpressions

/// The widgets write these classes in the browser, so the static output does not contain them.
let widgetClasses =
  set [ "subscribe"; "subscribe-field"; "subscribe-error"; "subscribe-status"; "is-failed"; "is-subscribed"; "subscribe-note"
        "comments"; "comments-count"; "comments-list"; "comment"; "comment-meta"; "comment-text"; "comment-form"
        "comment-field"; "comment-error"; "comment-status"; "is-done"; "comment-note"
        "unsubscribe"; "unsubscribe-status"; "unsubscribe-note" ]

let private classAttribute = Regex("""\sclass="([^"]*)""")

let htmlClasses (html: string) =
  set [
    for found in classAttribute.Matches html do
      yield! found.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
  ]

let private comment = Regex(@"/\*.*?\*/", RegexOptions.Singleline)
let private quoted = Regex(@"""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'")
let private declarations = Regex(@"\{[^{}]*\}")
let private selectorClass = Regex(@"\.(-?[A-Za-z_][\w-]*)")

let cssClasses (css: string) =
  let selectors = declarations.Replace(quoted.Replace(comment.Replace(css, ""), "''"), " ")
  set [ for found in selectorClass.Matches selectors -> found.Groups[1].Value ]

/// A rule for a code token class (`tk-`) needs no use, because a post can omit a token role.
let check (css: string) (pages: string seq) =
  let used = pages |> Seq.map htmlClasses |> Set.unionMany
  let ruled = cssClasses css

  [ for name in Set.difference (Set.difference used ruled) widgetClasses do
      $"The class '{name}' has no CSS rule."
    for name in Set.difference (Set.difference ruled used) widgetClasses do
      if not (name.StartsWith "tk-") then
        $"The CSS rule for '.{name}' has no use." ]
