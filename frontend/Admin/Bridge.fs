module TwoPoint.Admin.Bridge

open Browser
open Fable.Core

[<Import("broadcastResponseToMainFrame", "@azure/msal-browser/redirect-bridge")>]
let private broadcastResponseToMainFrame () : JS.Promise<unit> = jsNative

broadcastResponseToMainFrame ()
|> Promise.catch (fun error -> console.error ("admin: the redirect page has no response from Entra.", error))
|> Promise.start
