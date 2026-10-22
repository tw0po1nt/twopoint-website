[<RequireQualifiedAccess>]
module TwoPoint.Admin.Config

open Fable.Core

/// The origin of the API, with no `/` at the end.
[<Emit "ADMIN_CONFIG.api">]
let api () : string = jsNative

/// The origin of the admin, with no `/` at the end.
[<Emit "ADMIN_CONFIG.origin">]
let origin () : string = jsNative

/// The client ID of the "TwoPoint Admin Web" registration in Entra.
[<Emit "ADMIN_CONFIG.clientId">]
let clientId () : string = jsNative

[<Emit "ADMIN_CONFIG.authority">]
let authority () : string = jsNative

/// The scope of the API that each token request asks for.
[<Emit "ADMIN_CONFIG.scope">]
let scope () : string = jsNative
