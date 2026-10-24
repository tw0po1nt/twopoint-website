namespace TwoPoint.Admin

open Fable.Core

/// A Web Push subscription. The two keys are in base64url.
type PushKeys =
  {| endpoint: string
     p256dh: string
     auth: string |}

/// The Web Push of the browser. The admin uses the browser APIs, and the bundle test uses a stub.
type Push =
  /// "supported", "home-screen" (iOS with no PushManager), or "unsupported".
  abstract support: unit -> string
  /// "granted", "denied", or "default".
  abstract permission: unit -> string
  /// The subscription of this browser. It never asks for the permission. Resolves None when the browser has none.
  abstract subscription: unit -> JS.Promise<PushKeys option>
  /// Asks for the permission, then subscribes. A click must start it, or the browser refuses the prompt.
  /// Resolves None when the author does not grant the permission.
  abstract subscribe: unit -> JS.Promise<PushKeys option>
  /// The push worker calls the listener after a `pushsubscriptionchange` event.
  abstract onSubscriptionChange: listener: (unit -> unit) -> unit
