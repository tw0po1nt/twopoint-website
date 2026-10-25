namespace TwoPoint.Admin

open Fable.Core

/// A Web Push subscription. The two keys are in base64url.
type PushKeys =
  {| endpoint: string
     p256dh: string
     auth: string |}

type Push =
  /// "supported", "home-screen" (iOS with no PushManager), or "unsupported".
  abstract support: unit -> string
  /// "granted", "denied", or "default".
  abstract permission: unit -> string
  /// The public VAPID key in base64url, or an empty string when the build has none.
  abstract publicKey: unit -> string
  /// Never asks for the permission.
  abstract subscription: unit -> JS.Promise<PushKeys option>
  /// A click must start it, or the browser refuses the prompt.
  abstract subscribe: unit -> JS.Promise<PushKeys option>
  /// Ends the subscription of this browser.
  abstract unsubscribe: unit -> JS.Promise<unit>
  /// The push worker calls the listener after a `pushsubscriptionchange` event.
  abstract onSubscriptionChange: listener: (unit -> unit) -> unit
  /// The push worker calls the listener after it shows the notification of a new comment.
  abstract onNewComment: listener: (unit -> unit) -> unit
  /// The push worker calls the listener with the target after a click on a notification, when this page is open.
  abstract onOpenRequest: listener: (string -> unit) -> unit
  /// Removes the badge of the app icon, when the browser has one.
  abstract clearBadge: unit -> unit
