[<RequireQualifiedAccess>]
module TwoPoint.Admin.Notifications

type Support =
  | Supported
  /// iOS gives PushManager only to a Home Screen web app.
  | NeedsHomeScreen
  | Unsupported

type Permission =
  | Granted
  | Denied
  | Undecided

type Environment =
  { Support: Support
    Permission: Permission
    /// False when the admin config has no VAPID public key.
    Configured: bool }

type Registration =
  | Unregistered
  | Registering
  | Registered
  | RegistrationFailed of reason: string

type Devices =
  | Waiting
  | Loaded of Api.Device list
  | NotLoaded of reason: string

type Model =
  { Environment: Environment
    Registration: Registration
    Devices: Devices
    /// The id of the device that this browser registered.
    ThisDevice: string option
    Removing: string list
    RemoveFailure: string option }

type Msg =
  | Detected of Environment
  | TurnOnClicked
  /// The permission after the prompt, and the subscription. None when the author did not grant the permission.
  | Subscribed of Permission * Result<PushKeys option, string>
  /// None when the browser has no subscription to send.
  | RegisterAnswered of Result<Api.Device option, Api.Failure>
  | SubscriptionChanged
  | DevicesAnswered of Result<Api.Device list, Api.Failure>
  | RemoveClicked of deviceId: string
  | Removed of deviceId: string * Result<unit, Api.Failure>

type Effect =
  | Subscribe
  | Register of PushKeys
  /// Reads the subscription of the browser, with no prompt, and sends it.
  | Sync
  /// Sends the subscription of the browser, and subscribes first when the browser has none.
  | Resync
  | Remove of deviceId: string
  /// Ends the subscription of this browser.
  | Unsubscribe

let permissionOf (name: string) =
  match name with
  | "granted" -> Granted
  | "denied" -> Denied
  | _ -> Undecided

let supportOf (name: string) =
  match name with
  | "supported" -> Supported
  | "home-screen" -> NeedsHomeScreen
  | _ -> Unsupported

let detect (push: Push) =
  { Support = supportOf (push.support ())
    Permission = permissionOf (push.permission ())
    Configured = push.publicKey () <> "" }

/// A browser with the permission can send its subscription with no prompt.
let canSync (environment: Environment) =
  environment.Permission = Granted && environment.Support = Supported && environment.Configured

let init =
  { Environment =
      { Support = Unsupported
        Permission = Undecided
        Configured = false }
    Registration = Unregistered
    Devices = Waiting
    ThisDevice = None
    Removing = []
    RemoveFailure = None }

let reload (model: Model) =
  { model with
      Devices = Waiting
      RemoveFailure = None }

let update (msg: Msg) (model: Model) : Model * Effect option =
  match msg with
  | Detected environment ->
    { model with
        Environment = environment
        Registration = if canSync environment then Registering else Unregistered },
    (if canSync environment then Some Sync else None)
  | TurnOnClicked -> { model with Registration = Registering }, Some Subscribe
  | Subscribed(permission, Ok(Some keys)) ->
    { model with
        Environment = { model.Environment with Permission = permission } },
    Some(Register keys)
  | Subscribed(permission, Ok None) ->
    { model with
        Environment = { model.Environment with Permission = permission }
        Registration = Unregistered },
    None
  | Subscribed(permission, Error message) ->
    { model with
        Environment = { model.Environment with Permission = permission }
        Registration = RegistrationFailed message },
    None
  | RegisterAnswered(Ok(Some device)) ->
    { model with
        Registration = Registered
        ThisDevice = Some device.Id },
    None
  | RegisterAnswered(Ok None) -> { model with Registration = Unregistered }, None
  | RegisterAnswered(Error failure) -> { model with Registration = RegistrationFailed(Api.reason failure) }, None
  | SubscriptionChanged ->
    if canSync model.Environment then model, Some Resync else model, None
  | DevicesAnswered(Ok devices) -> { model with Devices = Loaded devices }, None
  | DevicesAnswered(Error failure) -> { model with Devices = NotLoaded(Api.reason failure) }, None
  | RemoveClicked deviceId ->
    { model with
        Removing = deviceId :: model.Removing
        RemoveFailure = None },
    Some(Remove deviceId)
  | Removed(deviceId, result) ->
    let removing = model.Removing |> List.filter (fun id -> id <> deviceId)

    match result with
    | Ok() ->
      let devices =
        match model.Devices with
        | Loaded devices -> Loaded(devices |> List.filter (fun device -> device.Id <> deviceId))
        | Waiting
        | NotLoaded _ -> model.Devices

      if model.ThisDevice = Some deviceId then
        { model with
            Removing = removing
            Devices = devices
            ThisDevice = None
            Registration = Unregistered },
        Some Unsubscribe
      else
        { model with
            Removing = removing
            Devices = devices },
        None
    | Error failure ->
      { model with
          Removing = removing
          RemoveFailure = Some(Api.reason failure) },
      None

type Section =
  | AddToHomeScreen
  | CannotNotify
  | NotConfigured
  | Blocked
  /// The browser can notify, and this browser has no registered subscription.
  | Off
  | On

let section (model: Model) =
  match model.Environment.Support with
  | NeedsHomeScreen -> AddToHomeScreen
  | Unsupported -> CannotNotify
  | Supported when not model.Environment.Configured -> NotConfigured
  | Supported ->
    match model.Environment.Permission, model.Registration with
    | Denied, _ -> Blocked
    | Granted, (Registering | Registered) -> On
    | (Granted | Undecided), _ -> Off

let sectionText (section: Section) =
  match section with
  | AddToHomeScreen -> "Add the admin to the Home Screen to turn on notifications."
  | CannotNotify -> "This browser cannot receive notifications."
  | NotConfigured -> "The admin config has no VAPID public key, so this build cannot turn on notifications."
  | Blocked -> "The browser blocks notifications for this site. Change the browser setting to allow them, then reload."
  | Off -> "This device gets no notifications."
  | On -> "This device gets notifications."

let registrationFailure (model: Model) =
  match model.Registration with
  | RegistrationFailed reason -> Some reason
  | Unregistered
  | Registering
  | Registered -> None

let isBusy (model: Model) = model.Registration = Registering

let emptyText = "No device is registered."

let addedText (device: Api.Device) = $"Added {Dates.dateText device.CreatedDate}"

let lastSeenText (device: Api.Device) = $"Last used {Dates.dateText device.LastSeenDate}"
