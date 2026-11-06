module TwoPoint.Admin.Tests.NotificationsTests

open TwoPoint.Admin

open Expecto

let private keys: PushKeys =
  {| endpoint = "https://push.example.com/send/abc"
     p256dh = "BNcRdre"
     auth = "tBHItJI" |}

let private device id label : Api.Device =
  { Id = id
    Label = label
    CreatedDate = "2026-03-01T12:00:00Z"
    LastSeenDate = "2026-03-02T08:30:00Z" }

let private mac = device "aaaa" "Safari on macOS"
let private phone = device "bbbb" "Safari on iOS"

let private environment support permission : Notifications.Environment =
  { Support = support
    Permission = permission
    Configured = true }

let private detect env =
  Notifications.update (Notifications.Detected env) Notifications.init |> fst

let private section support permission =
  Notifications.section (detect (environment support permission))

let private send msg model = Notifications.update msg model

let private started path env =
  let model, _ = Shell.init path
  let model, _ = Shell.update (Shell.Notifications(Notifications.Detected env)) model
  Shell.update (Shell.Session(Session.Started true)) model

[<Tests>]
let tests =
  testList "Notifications" [
    testList "the section" [
      test "the permission granted shows that the notifications are on" {
        Expect.equal (section Notifications.Supported Notifications.Granted) Notifications.On "On"
      }

      test "the permission denied tells the author to change the browser setting" {
        Expect.equal (section Notifications.Supported Notifications.Denied) Notifications.Blocked "Blocked"
        Expect.stringContains (Notifications.sectionText Notifications.Blocked) "browser setting" "The text"
      }

      test "no PushManager on iOS tells the author to add the admin to the Home Screen" {
        Expect.equal (section Notifications.NeedsHomeScreen Notifications.Undecided) Notifications.AddToHomeScreen "Home Screen"

        Expect.equal
          (Notifications.sectionText Notifications.AddToHomeScreen)
          "Add the admin to the Home Screen to turn on notifications."
          "The text"
      }

      test "no PushManager elsewhere says that the browser has no notifications" {
        Expect.equal (section Notifications.Unsupported Notifications.Undecided) Notifications.CannotNotify "Cannot"
      }

      test "an undecided permission offers the button" {
        Expect.equal (section Notifications.Supported Notifications.Undecided) Notifications.Off "Off"
      }

      test "a config with no VAPID key says that the setup is missing" {
        let model = detect { environment Notifications.Supported Notifications.Undecided with Configured = false }
        Expect.equal (Notifications.section model) Notifications.NotConfigured "Not configured"
      }
    ]

    testList "the device of this browser" [
      test "a load with the permission granted starts to register with no click" {
        let model = detect (environment Notifications.Supported Notifications.Granted)
        Expect.equal model.Registration Notifications.Registering "Registering"
      }

      test "a load with any other permission registers nothing" {
        for permission in [ Notifications.Undecided; Notifications.Denied ] do
          let model = detect (environment Notifications.Supported permission)
          Expect.equal model.Registration Notifications.Unregistered $"{permission}"
      }

      test "a click asks the browser to subscribe" {
        let model = detect (environment Notifications.Supported Notifications.Undecided)
        let model, effect = send Notifications.TurnOnClicked model
        Expect.equal effect (Some Notifications.Subscribe) "Subscribe"
        Expect.equal model.Registration Notifications.Registering "Registering"
      }

      test "a subscription goes to the backend" {
        let model = detect (environment Notifications.Supported Notifications.Undecided)
        let model, effect = send (Notifications.Subscribed(Notifications.Granted, Ok(Some keys))) model
        Expect.equal effect (Some(Notifications.Register keys)) "Register"
        Expect.equal model.Environment.Permission Notifications.Granted "Granted"
      }

      test "a refused permission shows the blocked section and sends nothing" {
        let model = detect (environment Notifications.Supported Notifications.Undecided)
        let model, effect = send (Notifications.Subscribed(Notifications.Denied, Ok None)) model
        Expect.isNone effect "No effect"
        Expect.equal (Notifications.section model) Notifications.Blocked "Blocked"
      }

      test "a dismissed prompt keeps the button" {
        let model = detect (environment Notifications.Supported Notifications.Undecided)
        let model, _ = send Notifications.TurnOnClicked model
        let model, _ = send (Notifications.Subscribed(Notifications.Undecided, Ok None)) model
        Expect.equal (Notifications.section model) Notifications.Off "Off"
      }

      test "a failed subscribe shows the reason" {
        let model = detect (environment Notifications.Supported Notifications.Granted)
        let model, _ = send (Notifications.Subscribed(Notifications.Granted, Error "The push service is down.")) model
        Expect.equal model.Registration (Notifications.RegistrationFailed "The push service is down.") "The reason"
        Expect.equal (Notifications.section model) Notifications.Off "The button comes back"
      }

      test "a registered device makes the section on" {
        let model = detect (environment Notifications.Supported Notifications.Granted)
        let model, _ = send (Notifications.RegisterAnswered(Ok(Some mac))) model
        Expect.equal model.Registration Notifications.Registered "Registered"
        Expect.equal (Notifications.section model) Notifications.On "On"
      }

      test "a granted load with no subscription offers the button" {
        let model = detect (environment Notifications.Supported Notifications.Granted)
        let model, _ = send (Notifications.RegisterAnswered(Ok None)) model
        Expect.equal (Notifications.section model) Notifications.Off "Off"
      }

      test "a refused registration shows the reason from the API" {
        let model = detect (environment Notifications.Supported Notifications.Granted)
        let model, _ = send (Notifications.RegisterAnswered(Error(Api.Failed "Bad endpoint."))) model
        Expect.equal model.Registration (Notifications.RegistrationFailed "Bad endpoint.") "The reason"
      }

      test "a changed subscription registers again, and subscribes if the browser has none" {
        let model = detect (environment Notifications.Supported Notifications.Granted)
        let _, effect = send Notifications.SubscriptionChanged model
        Expect.equal effect (Some Notifications.Resync) "Resync"
      }

      test "a changed subscription with no permission does nothing" {
        let model = detect (environment Notifications.Supported Notifications.Undecided)
        let _, effect = send Notifications.SubscriptionChanged model
        Expect.isNone effect "No effect"
      }

      test "a changed subscription with no VAPID key does nothing" {
        let model = detect { environment Notifications.Supported Notifications.Granted with Configured = false }
        let _, effect = send Notifications.SubscriptionChanged model
        Expect.isNone effect "No effect"
      }

      test "a load that can sync asks for a sync, and another load asks for nothing" {
        let _, granted = Notifications.update (Notifications.Detected(environment Notifications.Supported Notifications.Granted)) Notifications.init
        Expect.equal granted (Some Notifications.Sync) "Sync"

        for env in
          [ environment Notifications.Supported Notifications.Undecided
            environment Notifications.Supported Notifications.Denied
            environment Notifications.NeedsHomeScreen Notifications.Granted
            { environment Notifications.Supported Notifications.Granted with Configured = false } ] do
          Expect.isNone (snd (Notifications.update (Notifications.Detected env) Notifications.init)) $"{env}"
      }

      test "a registered device is the device of this browser" {
        let model = detect (environment Notifications.Supported Notifications.Granted)
        let model, _ = send (Notifications.RegisterAnswered(Ok(Some mac))) model
        Expect.equal model.ThisDevice (Some "aaaa") "This device"
      }

      test "the removal of the device of this browser unsubscribes it, and the section is off" {
        let model = detect (environment Notifications.Supported Notifications.Granted)
        let model, _ = send (Notifications.RegisterAnswered(Ok(Some mac))) model
        let model, _ = send (Notifications.DevicesAnswered(Ok [ mac; phone ])) model
        let model, _ = send (Notifications.RemoveClicked "aaaa") model
        let model, effect = send (Notifications.Removed("aaaa", Ok())) model
        Expect.equal effect (Some Notifications.Unsubscribe) "Unsubscribe"
        Expect.equal model.ThisDevice None "No device"
        Expect.equal (Notifications.section model) Notifications.Off "Off"
      }

      test "the removal of another device keeps the subscription" {
        let model = detect (environment Notifications.Supported Notifications.Granted)
        let model, _ = send (Notifications.RegisterAnswered(Ok(Some mac))) model
        let model, _ = send (Notifications.DevicesAnswered(Ok [ mac; phone ])) model
        let model, _ = send (Notifications.RemoveClicked "bbbb") model
        let model, effect = send (Notifications.Removed("bbbb", Ok())) model
        Expect.isNone effect "No effect"
        Expect.equal (Notifications.section model) Notifications.On "On"
      }

      test "a failed removal of the device of this browser keeps the subscription" {
        let model = detect (environment Notifications.Supported Notifications.Granted)
        let model, _ = send (Notifications.RegisterAnswered(Ok(Some mac))) model
        let model, effect = send (Notifications.Removed("aaaa", Error Api.Unanswered)) model
        Expect.isNone effect "No effect"
        Expect.equal model.ThisDevice (Some "aaaa") "Still this device"
      }
    ]

    testList "the device list" [
      test "the list waits, then shows the devices" {
        Expect.equal Notifications.init.Devices Notifications.Waiting "Waiting"
        let model, _ = send (Notifications.DevicesAnswered(Ok [ mac; phone ])) Notifications.init
        Expect.equal model.Devices (Notifications.Loaded [ mac; phone ]) "Loaded"
      }

      test "a failed list shows the reason" {
        let model, _ = send (Notifications.DevicesAnswered(Error Api.Unanswered)) Notifications.init
        Expect.equal model.Devices (Notifications.NotLoaded "The server didn't respond. Check your connection and try again.") "The reason"
      }

      test "Remove asks the backend to remove the device" {
        let model, _ = send (Notifications.DevicesAnswered(Ok [ mac; phone ])) Notifications.init
        let model, effect = send (Notifications.RemoveClicked "aaaa") model
        Expect.equal effect (Some(Notifications.Remove "aaaa")) "Remove"
        Expect.equal model.Removing [ "aaaa" ] "The row waits"
      }

      test "a removed device leaves the list" {
        let model, _ = send (Notifications.DevicesAnswered(Ok [ mac; phone ])) Notifications.init
        let model, _ = send (Notifications.RemoveClicked "aaaa") model
        let model, _ = send (Notifications.Removed("aaaa", Ok())) model
        Expect.equal model.Devices (Notifications.Loaded [ phone ]) "One device"
        Expect.isEmpty model.Removing "Nothing waits"
      }

      test "a failed removal keeps the device and shows the reason" {
        let model, _ = send (Notifications.DevicesAnswered(Ok [ mac ])) Notifications.init
        let model, _ = send (Notifications.RemoveClicked "aaaa") model
        let model, _ = send (Notifications.Removed("aaaa", Error(Api.Failed "No such device."))) model
        Expect.equal model.Devices (Notifications.Loaded [ mac ]) "Still there"
        Expect.equal model.RemoveFailure (Some "No such device.") "The reason"
        Expect.isEmpty model.Removing "Nothing waits"
      }

      test "the label, the added date, and the last use are the text of a row" {
        Expect.equal (Notifications.addedText mac) "Added 2026-03-01" "Added"
        Expect.equal (Notifications.lastSeenText mac) "Last used 2026-03-02" "Last used"
      }
    ]

    testList "the shell" [
      test "a start on the settings page loads the devices" {
        let _, effect = started "/settings" (environment Notifications.Supported Notifications.Undecided)
        Expect.equal effect (Some(Shell.Send [ Shell.LoadQueue; Shell.LoadDevices ])) "The devices"
      }

      test "a start with the permission granted sends the subscription again with no click" {
        let _, effect = started "/" (environment Notifications.Supported Notifications.Granted)
        Expect.equal effect (Some(Shell.Send [ Shell.LoadQueue; Shell.LoadDashboard; Shell.LoadSiteDeploy; Shell.SyncDevice ])) "The sync"
      }

      test "a start with the permission undecided or denied sends no subscription" {
        for permission in [ Notifications.Undecided; Notifications.Denied ] do
          let _, effect = started "/" (environment Notifications.Supported permission)
          Expect.equal effect (Some(Shell.Send [ Shell.LoadQueue; Shell.LoadDashboard; Shell.LoadSiteDeploy ])) $"{permission}"
      }

      test "the click is an effect of the shell, so the browser keeps the user gesture" {
        let model, _ = started "/settings" (environment Notifications.Supported Notifications.Undecided)
        let _, effect = Shell.update (Shell.Notifications Notifications.TurnOnClicked) model
        Expect.equal effect (Some Shell.Subscribe) "Subscribe"
      }

      test "a subscription goes to the API with a call" {
        let model, _ = started "/settings" (environment Notifications.Supported Notifications.Undecided)
        let _, effect = Shell.update (Shell.Notifications(Notifications.Subscribed(Notifications.Granted, Ok(Some keys)))) model
        Expect.equal effect (Some(Shell.Send [ Shell.RegisterDevice keys ])) "Register"
      }

      test "a registered device on the settings page loads the list again" {
        let model, _ = started "/settings" (environment Notifications.Supported Notifications.Granted)
        let _, effect = Shell.update (Shell.Notifications(Notifications.RegisterAnswered(Ok(Some mac)))) model
        Expect.equal effect (Some(Shell.Send [ Shell.LoadDevices ])) "The list"
      }

      test "a registered device on another page loads nothing" {
        let model, _ = started "/" (environment Notifications.Supported Notifications.Granted)
        let _, effect = Shell.update (Shell.Notifications(Notifications.RegisterAnswered(Ok(Some mac)))) model
        Expect.isNone effect "No call"
      }

      test "Remove sends the delete call" {
        let model, _ = started "/settings" (environment Notifications.Supported Notifications.Undecided)
        let model, _ = Shell.update (Shell.Notifications(Notifications.DevicesAnswered(Ok [ mac ]))) model
        let _, effect = Shell.update (Shell.Notifications(Notifications.RemoveClicked "aaaa")) model
        Expect.equal effect (Some(Shell.Send [ Shell.RemoveDevice "aaaa" ])) "Remove"
      }

      test "a changed subscription sends the resync call once the session is active" {
        let model, _ = started "/" (environment Notifications.Supported Notifications.Granted)
        let _, effect = Shell.update (Shell.Notifications Notifications.SubscriptionChanged) model
        Expect.equal effect (Some(Shell.Send [ Shell.ResyncDevice ])) "Resync"
      }

      test "a changed subscription before the session is active sends nothing, because the start syncs" {
        let model, _ = Shell.init "/"
        let model, _ = Shell.update (Shell.Notifications(Notifications.Detected(environment Notifications.Supported Notifications.Granted))) model
        let _, effect = Shell.update (Shell.Notifications Notifications.SubscriptionChanged) model
        Expect.isNone effect "No call"
      }

      test "the removal of the device of this browser unsubscribes the browser" {
        let model, _ = started "/settings" (environment Notifications.Supported Notifications.Granted)
        let model, _ = Shell.update (Shell.Notifications(Notifications.RegisterAnswered(Ok(Some mac)))) model
        let model, _ = Shell.update (Shell.Notifications(Notifications.DevicesAnswered(Ok [ mac ]))) model
        let _, effect = Shell.update (Shell.Notifications(Notifications.Removed("aaaa", Ok()))) model
        Expect.equal effect (Some Shell.Unsubscribe) "Unsubscribe"
      }

      test "a refused token on the device list shows the banner" {
        let model, _ = started "/settings" (environment Notifications.Supported Notifications.Undecided)
        let model, _ = Shell.update (Shell.Notifications(Notifications.DevicesAnswered(Error Api.Refused))) model
        Expect.equal model.Session.Status Session.Refused "Refused"
      }
    ]
  ]
