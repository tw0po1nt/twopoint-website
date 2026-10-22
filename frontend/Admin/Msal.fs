[<RequireQualifiedAccess>]
module TwoPoint.Admin.Msal

open Fable.Core

[<AllowNullLiteral>]
type AccountInfo =
  abstract homeAccountId: string
  abstract username: string

[<AllowNullLiteral>]
type AuthenticationResult =
  abstract accessToken: string
  abstract account: AccountInfo

type PublicClientApplication =
  /// Each other member fails until this promise resolves.
  abstract initialize: unit -> JS.Promise<unit>
  /// The result is null when the URL and the session storage have no response from Entra.
  abstract handleRedirectPromise: unit -> JS.Promise<AuthenticationResult>
  abstract getAllAccounts: unit -> AccountInfo array
  /// The result is null when no account is active.
  abstract getActiveAccount: unit -> AccountInfo
  abstract setActiveAccount: account: AccountInfo -> unit
  /// The promise resolves when the page starts to leave for Entra.
  abstract loginRedirect: request: {| scopes: string array |} -> JS.Promise<unit>
  abstract acquireTokenSilent: request: {| scopes: string array; account: AccountInfo |} -> JS.Promise<AuthenticationResult>
  /// With a null account, Entra asks the user to choose an account.
  abstract acquireTokenPopup: request: {| scopes: string array; account: AccountInfo |} -> JS.Promise<AuthenticationResult>

type Configuration =
  {| auth: {| clientId: string; authority: string; redirectUri: string |}
     cache: {| cacheLocation: string |} |}

[<Import("PublicClientApplication", "@azure/msal-browser")>]
let private publicClientApplicationClass: obj = jsNative

[<Emit "new $0($1)">]
let private construct (constructor: obj) (options: obj) : 'T = jsNative

let publicClientApplication (configuration: Configuration) : PublicClientApplication =
  construct publicClientApplicationClass configuration

