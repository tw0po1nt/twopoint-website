namespace TwoPoint.Dependencies.Subscriptions

open TwoPoint.Core.Posts
open TwoPoint.Core.Shared

open System
open System.Net
open System.Text.RegularExpressions

module internal SubscriptionEmails =

  let welcomeSubject = "Welcome to TwoPoint!"

  let private sans = "'Inter', -apple-system, 'Segoe UI', Helvetica, Arial, sans-serif"
  let private mono = "'JetBrains Mono', Consolas, Menlo, monospace"

  /// <summary>
  /// The intro that goes above the latest post in a welcome
  /// </summary>
  /// <remarks>
  /// The newsletter layout puts the intro in a dark terminal panel in light mode and in dark mode, so the intro
  /// uses light text. Email clients ignore most style blocks, so each element has its styles inline.
  /// </remarks>
  /// <param name="name">The name of the subscription, if the subscriber gave one</param>
  let welcomeIntro (name : string option) =
    let name = name |> Option.map _.Trim() |> Option.filter (String.IsNullOrWhiteSpace >> not)

    let greeting =
      name
      |> Option.map (fun name -> $"Hey {WebUtility.HtmlEncode name}, thanks for subscribing.")
      |> Option.defaultValue "Hey there, thanks for subscribing."

    // The command is decoration. A name with a space gets quotes, as a shell argument would.
    let command =
      name
      |> Option.map _.ToLowerInvariant()
      |> Option.map (fun name -> if name |> Seq.exists Char.IsWhiteSpace then $"\"{name}\"" else name)
      |> Option.map (fun name -> $"./welcome --to {WebUtility.HtmlEncode name}")
      |> Option.defaultValue "./welcome"

    $$"""
<div class="tp-px" style="padding: 22px 32px 16px; font-family: {{mono}}; font-size: 14px; line-height: 1.75; color: #e1e4e8;">
  <p style="margin: 0 0 14px;"><span style="color: #28c840;">matt@twopoint</span> <span style="color: #8b949e;">$</span> {{command}}</p>
  <p style="margin: 0 0 14px; font-family: {{sans}}; font-size: 26px; font-weight: 700; line-height: 1.3; color: #ffffff;">{{greeting}}</p>
  <p style="margin: 0 0 14px; font-family: {{sans}}; font-size: 16px; line-height: 1.7; color: #e1e4e8;">I write about F#, functional programming, and whatever else I find interesting. To get you started, here is my latest post. You'll be notified of new posts as soon as they are published.</p>
  <p style="margin: 0; color: #8b949e;">// Matt <span style="color: #d62b2b;">&#9613;</span></p>
</div>
"""

  let newPostSubject (post : Post) = post.Info.Title |> NonEmptyString.value

  /// <param name="text">Plain text from the author. A blank line starts a new paragraph. <c>None</c> gives an empty
  /// intro slot.</param>
  let newPostIntro (text : NonEmptyString option) =
    match text with
    | None -> ""
    | Some text ->
      let paragraphs =
        Regex.Split(NonEmptyString.value text, @"\r?\n[ \t]*\r?\n")
        |> Array.map _.Trim()
        |> Array.filter (String.IsNullOrWhiteSpace >> not)
      let last = paragraphs.Length - 1
      let html =
        paragraphs
        |> Array.mapi (fun index paragraph ->
          let margin = if index = last then "0" else "0 0 14px"
          $"""  <p style="margin: {margin}; font-family: {sans}; font-size: 16px; line-height: 1.7; color: #e1e4e8;">{WebUtility.HtmlEncode paragraph}</p>"""
        )
        |> String.concat "\n"

      $$"""
<div class="tp-px" style="padding: 22px 32px 16px; font-family: {{mono}}; font-size: 14px; line-height: 1.75; color: #e1e4e8;">
{{html}}
</div>
"""
