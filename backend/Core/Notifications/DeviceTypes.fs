namespace TwoPoint.Core.Notifications

open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open System
open System.Security.Cryptography
open System.Text

type PushEndpoint =
  private | PushEndpoint of string

  override this.ToString() =
    let (PushEndpoint endpoint) = this
    endpoint

module PushEndpoint =
  let private httpsUrl (value : string) =
    match value with
    | Uri uri when uri.Scheme = Uri.UriSchemeHttps -> Some value
    | _ -> None

  let create =
    Constrained.String.tryParse httpsUrl $"'{nameof PushEndpoint}' must be an https URL" PushEndpoint

/// The SHA-256 of the endpoint, in lowercase hexadecimal
type DeviceId =
  private | DeviceId of string

  override this.ToString() =
    let (DeviceId id) = this
    id

module DeviceId =
  let ofEndpoint (endpoint : PushEndpoint) =
    endpoint.ToString()
    |> Encoding.UTF8.GetBytes
    |> SHA256.HashData
    |> Convert.ToHexStringLower
    |> DeviceId

  let create =
    Constrained.String.tryParse
      (regexParser "^[0-9a-f]{64}$")
      $"'{nameof DeviceId}' must be 64 lowercase hexadecimal characters"
      DeviceId

module DeviceLabel =

  let private contains (part : string) (userAgent : string) =
    userAgent.Contains(part, StringComparison.OrdinalIgnoreCase)

  let private browser userAgent =
    [ "Edg/", "Edge"
      "EdgA/", "Edge"
      "EdgiOS/", "Edge"
      "Firefox/", "Firefox"
      "FxiOS/", "Firefox"
      "Chrome/", "Chrome"
      "CriOS/", "Chrome"
      "AppleWebKit/", "Safari" ]
    |> List.tryFind (fun (part, _) -> contains part userAgent)
    |> Option.map snd

  // The iPhone and iPad user agents also contain "Mac OS X", so they come first.
  let private operatingSystem userAgent =
    [ "iPhone", "iOS"
      "iPad", "iPadOS"
      "Android", "Android"
      "Windows", "Windows"
      "CrOS", "ChromeOS"
      "Macintosh", "macOS"
      "Linux", "Linux" ]
    |> List.tryFind (fun (part, _) -> contains part userAgent)
    |> Option.map snd

  /// A label such as "Safari on macOS"
  let ofUserAgent (userAgent : string) : NonEmptyString =
    let userAgent = userAgent |> Option.ofObj |> Option.defaultValue ""
    let label =
      match browser userAgent, operatingSystem userAgent with
      | Some browser, Some os -> $"{browser} on {os}"
      | Some browser, None -> browser
      | None, Some os -> os
      | None, None -> "Unknown device"
    NonEmptyString.Unsafe.create None label

type RegisteredDevice =
  { UserId : UserExternalId
    Id : DeviceId
    Endpoint : PushEndpoint
    P256dh : NonEmptyString
    Auth : NonEmptyString
    Label : NonEmptyString
    CreatedDate : DateTime
    LastSeenDate : DateTime }
