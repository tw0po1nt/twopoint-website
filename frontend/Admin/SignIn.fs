namespace TwoPoint.Admin

open Fable.Core

/// The sign-in of the author. The admin uses MSAL, and the bundle test uses a stub.
type SignIn =
  /// Resolves true when the browser has a session of the author, after it reads a response from Entra.
  abstract start: unit -> JS.Promise<bool>
  /// Entra asks which account to use, also when the browser has a session of another account.
  abstract signIn: unit -> JS.Promise<unit>
  /// Resolves None when Entra needs the author to sign in again.
  abstract token: unit -> JS.Promise<string option>
  /// Opens the Entra sign-in in a popup. A click must start it, or the browser blocks the popup.
  abstract signInByPopup: unit -> JS.Promise<unit>
