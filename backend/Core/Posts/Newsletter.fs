namespace TwoPoint.Core.Posts

open System
open System.Net

/// <summary>
/// The email version of a post, as the frontend build renders it
/// </summary>
/// <remarks>
/// A newsletter has slots that each delivery fills in. A newsletter without them cannot exist, because an email
/// without an unsubscribe link must never reach a subscription.
/// </remarks>
type Newsletter =
  private | Newsletter of string

module Newsletter =

  /// The frontend newsletter layout writes these tokens. Keep them identical to `EmailBlogLayout.astro`.
  [<Literal>]
  let IntroSlot = "%%TWOPOINT_INTRO%%"

  [<Literal>]
  let UnsubscribeUrlSlot = "%%TWOPOINT_UNSUBSCRIBE_URL%%"

  let create (html : string) =
    let missing =
      [ IntroSlot; UnsubscribeUrlSlot ]
      |> List.filter (fun slot -> not (html.Contains(slot, StringComparison.Ordinal)))
    match missing with
    | [] -> Ok (Newsletter html)
    | slots -> Error $"""Newsletter is missing the slots: {String.Join(", ", slots)}"""

  /// <summary>
  /// Fill the slots of a newsletter for one delivery
  /// </summary>
  /// <param name="intro">HTML to put above the post. Use an empty string for no intro.</param>
  /// <param name="unsubscribeUrl">The URL that cancels the subscription of the recipient</param>
  /// <param name="newsletter">The newsletter to fill</param>
  let fill (intro : string) (unsubscribeUrl : Uri) (Newsletter html) =
    html
      .Replace(IntroSlot, intro, StringComparison.Ordinal)
      .Replace(UnsubscribeUrlSlot, WebUtility.HtmlEncode unsubscribeUrl.AbsoluteUri, StringComparison.Ordinal)
