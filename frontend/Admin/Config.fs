[<RequireQualifiedAccess>]
module TwoPoint.Admin.Config

open Fable.Core

/// The origin of the API, with no `/` at the end. The build writes the value of its mode from `config.json`.
[<Emit "ADMIN_CONFIG.api">]
let api () : string = jsNative

/// The origin of the admin, with no `/` at the end. The build writes the value of its mode from `config.json`.
[<Emit "ADMIN_CONFIG.origin">]
let origin () : string = jsNative
