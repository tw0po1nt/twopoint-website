module TwoPoint.Site.Widgets.DevReload

open Browser
open Fable.Core
open Fable.Core.JsInterop

[<Emit("new EventSource($0)")>]
let private eventSource (url: string) : obj = jsNative

let private source = eventSource "/__dev/reload"

source?onmessage <- fun _ -> window.location.reload ()
