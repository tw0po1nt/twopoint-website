module TwoPoint.Core.Tests.DeviceTests

open TwoPoint.Core.Notifications
open TwoPoint.Core.Notifications.Actions
open TwoPoint.Core.Notifications.Queries
open TwoPoint.Core.Util

open Expecto
open FsToolkit.ErrorHandling

open System
open System.Collections.Generic
open System.Threading

let private userId = "6f1c1b5e-3d2a-4a43-9c6b-0d7a5b0e9a11"
let private otherUserId = "0b9f8a3c-7e5d-4c21-8f4a-2a6d1c3e5b77"
let private endpoint = "https://push.example.com/send/abc123"
let private safariOnMac =
  "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Safari/605.1.15"
let private firstSeen = DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc)
let private secondSeen = DateTime(2026, 3, 2, 8, 30, 0, DateTimeKind.Utc)

type private FakeRegisterDependencies(rows : Dictionary<string, RegisteredDevice>, utcNow : DateTime) =
  let key (userId : TwoPoint.Core.Posts.UserExternalId) (deviceId : DeviceId) = $"{userId}/{deviceId}"

  interface RegisterPushDevice.Dependencies with
    member _.GetDevice userId deviceId =
      match rows.TryGetValue (key userId deviceId) with
      | true, device -> CancellableTaskResult.singleton (Some device)
      | _ -> CancellableTaskResult.singleton None
    member _.GetUtcNow () = utcNow
    member _.SaveDevice device =
      rows[key device.UserId device.Id] <- device
      CancellableTaskResult.singleton device

let private registerDto userAgent : RegisterPushDevice.Dto =
  { UserExternalId = userId
    Endpoint = endpoint
    P256dh = "BNcRdreALRFXTkOOUHK1EtK2wtaz5Ry4YfYCA_0QTpQtUbVlUls0VJXg7A8u-Ts1XbjhazAkj7I99e8QcYP7DkM"
    Auth = "tBHItJI5svbpez7KI4CCXg"
    UserAgent = userAgent }

let private register (rows : Dictionary<string, RegisteredDevice>) utcNow dto =
  let dependencies = FakeRegisterDependencies(rows, utcNow)
  (RegisterPushDevice.withDependencies dependencies dto CancellationToken.None).Result

type private FakeListDependencies(devices : RegisteredDevice list) =
  interface ListPushDevices.Dependencies with
    member _.GetDevicesOfUser userId =
      CancellableTaskResult.singleton (devices |> List.filter (fun d -> d.UserId = userId))

type private FakeRemoveDependencies(rows : List<RegisteredDevice>) =
  interface RemovePushDevice.Dependencies with
    member _.GetDevice userId deviceId =
      CancellableTaskResult.singleton (rows |> Seq.tryFind (fun d -> d.UserId = userId && d.Id = deviceId))
    member _.DeleteDevice device =
      rows.Remove device |> ignore
      CancellableTaskResult.singleton ()

let private remove (rows : List<RegisteredDevice>) user id =
  let dto : RemovePushDevice.Dto = { UserExternalId = user; DeviceId = id }
  (RemovePushDevice.withDependencies (FakeRemoveDependencies rows) dto CancellationToken.None).Result

let private registerTests =
  testList "RegisterDevice" [
    test "a new endpoint makes a row with the label from the user agent" {
      let rows = Dictionary()

      match register rows firstSeen (registerDto safariOnMac) with
      | Ok (RegisterPushDevice.Event.Registered device) ->
        Expect.equal (string device.Label) "Safari on macOS" "Expected the label"
        Expect.equal device.CreatedDate firstSeen "Expected the created date"
        Expect.equal device.LastSeenDate firstSeen "Expected the last seen date"
        Expect.equal rows.Count 1 "Expected one row"
      | other -> failtestf "Expected a registration, got %A" other
    }

    test "a second register of one endpoint updates LastSeenDate and makes no second row" {
      let rows = Dictionary()
      register rows firstSeen (registerDto safariOnMac) |> ignore

      match register rows secondSeen (registerDto safariOnMac) with
      | Ok (RegisterPushDevice.Event.Registered device) ->
        Expect.equal rows.Count 1 "Expected still one row"
        Expect.equal device.CreatedDate firstSeen "Expected the first created date"
        Expect.equal device.LastSeenDate secondSeen "Expected the new last seen date"
      | other -> failtestf "Expected a registration, got %A" other
    }

    test "a second register replaces the keys of the subscription" {
      let rows = Dictionary()
      register rows firstSeen (registerDto safariOnMac) |> ignore

      let changed = { registerDto safariOnMac with Auth = "newAuthSecret123456789" }
      match register rows secondSeen changed with
      | Ok (RegisterPushDevice.Event.Registered device) ->
        Expect.equal (string device.Auth) "newAuthSecret123456789" "Expected the new auth"
      | other -> failtestf "Expected a registration, got %A" other
    }

    test "the same endpoint of two users makes two rows" {
      let rows = Dictionary()
      register rows firstSeen (registerDto safariOnMac) |> ignore
      register rows firstSeen { registerDto safariOnMac with UserExternalId = otherUserId } |> ignore
      Expect.equal rows.Count 2 "Expected two rows"
    }

    test "the row key is the SHA-256 of the endpoint" {
      let rows = Dictionary()

      match register rows firstSeen (registerDto safariOnMac) with
      | Ok (RegisterPushDevice.Event.Registered device) ->
        let expected =
          Security.Cryptography.SHA256.HashData(Text.Encoding.UTF8.GetBytes endpoint) |> Convert.ToHexStringLower
        Expect.equal (string device.Id) expected "Expected the hash"
      | other -> failtestf "Expected a registration, got %A" other
    }

    test "an endpoint that is not an https URL is a validation error" {
      let rows = Dictionary()

      match register rows firstSeen { registerDto safariOnMac with Endpoint = "http://push.example.com/x" } with
      | Error (ActionError.Validation _) -> Expect.equal rows.Count 0 "Expected no row"
      | other -> failtestf "Expected a validation error, got %A" other
    }

    test "an empty key is a validation error" {
      let rows = Dictionary()

      match register rows firstSeen { registerDto safariOnMac with P256dh = "" } with
      | Error (ActionError.Validation _) -> Expect.equal rows.Count 0 "Expected no row"
      | other -> failtestf "Expected a validation error, got %A" other
    }
  ]

let private labelTests =
  let label userAgent = DeviceLabel.ofUserAgent userAgent |> string

  testList "DeviceLabel" [
    test "Safari on macOS" { Expect.equal (label safariOnMac) "Safari on macOS" "" }
    test "Chrome on Windows" {
      let ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36"
      Expect.equal (label ua) "Chrome on Windows" ""
    }
    test "Edge on Windows" {
      let ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36 Edg/126.0.0.0"
      Expect.equal (label ua) "Edge on Windows" ""
    }
    test "Firefox on Linux" {
      let ua = "Mozilla/5.0 (X11; Linux x86_64; rv:127.0) Gecko/20100101 Firefox/127.0"
      Expect.equal (label ua) "Firefox on Linux" ""
    }
    test "Chrome on Android" {
      let ua = "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Mobile Safari/537.36"
      Expect.equal (label ua) "Chrome on Android" ""
    }
    test "a Home Screen web app on iPhone is Safari on iOS" {
      let ua = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_4 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148"
      Expect.equal (label ua) "Safari on iOS" ""
    }
    test "an unknown user agent gets a plain label" { Expect.equal (label "") "Unknown device" "" }
  ]

let private deviceOf user url (created : DateTime) =
  let rows = Dictionary()
  match register rows created { registerDto safariOnMac with UserExternalId = user; Endpoint = url } with
  | Ok (RegisterPushDevice.Event.Registered device) -> device
  | other -> failwithf "Expected a registration, got %A" other

let private listTests =
  let older = deviceOf userId "https://push.example.com/a" firstSeen
  let newer = deviceOf userId "https://push.example.com/b" secondSeen
  let foreign = deviceOf otherUserId "https://push.example.com/c" secondSeen

  testList "ListDevices" [
    test "the list has the devices of the user, newest first" {
      let query = ListPushDevices.withDependencies (FakeListDependencies [ older; foreign; newer ])

      match (query userId CancellationToken.None).Result with
      | Ok devices -> Expect.equal devices [ newer; older ] "Expected the user's devices, newest first"
      | other -> failtestf "Expected Ok, got %A" other
    }

    test "an invalid user id is a validation error" {
      let query = ListPushDevices.withDependencies (FakeListDependencies [])

      match (query "nope" CancellationToken.None).Result with
      | Error (QueryError.Validation _) -> ()
      | other -> failtestf "Expected a validation error, got %A" other
    }
  ]

let private removeTests =
  let device = deviceOf userId endpoint firstSeen

  testList "RemoveDevice" [
    test "a removal deletes the device" {
      let rows = List [ device ]

      match remove rows userId (string device.Id) with
      | Ok (RemovePushDevice.Event.Removed removed) ->
        Expect.equal removed device "Expected the removed device"
        Expect.equal rows.Count 0 "Expected no row"
      | other -> failtestf "Expected a removal, got %A" other
    }

    test "an unknown device is not found" {
      let rows = List [ device ]

      match remove rows userId (String.replicate 64 "0") with
      | Error (ActionError.Logic (RemovePushDevice.Error.DeviceNotFound _)) ->
        Expect.equal rows.Count 1 "Expected the row to stay"
      | other -> failtestf "Expected not found, got %A" other
    }

    test "a device of another user is not found" {
      let rows = List [ device ]

      match remove rows otherUserId (string device.Id) with
      | Error (ActionError.Logic (RemovePushDevice.Error.DeviceNotFound _)) ->
        Expect.equal rows.Count 1 "Expected the row to stay"
      | other -> failtestf "Expected not found, got %A" other
    }

    test "a device id that is not a SHA-256 is a validation error" {
      match remove (List [ device ]) userId "abc" with
      | Error (ActionError.Validation _) -> ()
      | other -> failtestf "Expected a validation error, got %A" other
    }
  ]

[<Tests>]
let tests = testList "Devices" [ registerTests; labelTests; listTests; removeTests ]
