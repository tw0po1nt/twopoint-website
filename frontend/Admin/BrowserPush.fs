[<RequireQualifiedAccess>]
module TwoPoint.Admin.BrowserPush

open Fable.Core

[<Emit "typeof PushManager !== 'undefined' && 'serviceWorker' in navigator && typeof Notification !== 'undefined'">]
let private hasPushManager () : bool = jsNative

[<Emit "/iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1)">]
let private isIos () : bool = jsNative

[<Emit "typeof Notification === 'undefined' ? 'default' : Notification.permission">]
let private notificationPermission () : string = jsNative

[<Emit "Notification.requestPermission()">]
let private requestPermission () : JS.Promise<string> = jsNative

/// The worker handles `pushsubscriptionchange`, so the page registers it at each load.
[<Emit "navigator.serviceWorker.register('/sw.js').then(() => navigator.serviceWorker.ready)">]
let private ready () : JS.Promise<obj> = jsNative

[<Emit "$0.pushManager.getSubscription()">]
let private getSubscription (registration: obj) : JS.Promise<obj> = jsNative

[<Emit "$0.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: Uint8Array.from(atob($1.replace(/-/g, '+').replace(/_/g, '/')), (c) => c.charCodeAt(0)) })">]
let private subscribeWith (registration: obj) (publicKey: string) : JS.Promise<obj> = jsNative

type private Json =
  abstract endpoint: string
  abstract keys: {| p256dh: string; auth: string |}

[<Emit "$0.toJSON()">]
let private toJson (subscription: obj) : Json = jsNative

[<Emit "navigator.serviceWorker.addEventListener('message', (event) => { if (event.data && event.data.type === 'pushsubscriptionchange') $0() })">]
let private onWorkerMessage (listener: unit -> unit) : unit = jsNative

let private keysOf (subscription: obj) : PushKeys =
  let json = toJson subscription

  {| endpoint = json.endpoint
     p256dh = json.keys.p256dh
     auth = json.keys.auth |}

let create () : Push =
  { new Push with
      member _.support() =
        if hasPushManager () then "supported"
        elif isIos () then "home-screen"
        else "unsupported"

      member _.permission() = notificationPermission ()

      member _.subscription() =
        ready ()
        |> Promise.bind getSubscription
        |> Promise.map (fun subscription ->
          match subscription with
          | null -> None
          | subscription -> Some(keysOf subscription))

      member _.subscribe() =
        // The prompt must be the first call of the click, or Safari refuses it.
        requestPermission ()
        |> Promise.bind (fun permission ->
          if permission = "granted" then
            ready ()
            |> Promise.bind (fun registration ->
              getSubscription registration
              |> Promise.bind (fun existing ->
                match existing with
                | null -> subscribeWith registration (Config.vapidPublicKey ())
                | existing -> Promise.lift existing))
            |> Promise.map (keysOf >> Some)
          else
            Promise.lift None)

      member _.onSubscriptionChange listener =
        if hasPushManager () then
          onWorkerMessage listener }
