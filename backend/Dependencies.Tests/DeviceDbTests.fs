module TwoPoint.Dependencies.Tests.DeviceDbTests

open TwoPoint.Core.Notifications
open TwoPoint.Core.Notifications.Actions
open TwoPoint.Dependencies.Notifications

open Expecto

open System

let private seen = DateTime(2026, 3, 2, 8, 30, 0, DateTimeKind.Utc)

let private device =
  let dto : RegisterPushDevice.Dto =
    { UserExternalId = "6f1c1b5e-3d2a-4a43-9c6b-0d7a5b0e9a11"
      Endpoint = "https://push.example.com/send/abc123"
      P256dh = "BNcRdreALRFXTkOOUHK1EtK2wtaz5Ry4YfYCA_0QTpQtUbVlUls0VJXg7A8u-Ts1XbjhazAkj7I99e8QcYP7DkM"
      Auth = "tBHItJI5svbpez7KI4CCXg"
      UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Safari/605.1.15" }
  match RegisterPushDevice.create dto with
  | Ok input -> RegisterPushDevice.decide seen None input
  | Error errors -> failwithf "Expected an input, got %A" errors

[<Tests>]
let tests =
  testList "DbRegisteredDevice" [
    test "the entity has the user as partition key and the endpoint hash as row key" {
      let entity = (DbRegisteredDevice.FromDevice device).ToEntity()
      Expect.equal entity.PartitionKey "6f1c1b5e-3d2a-4a43-9c6b-0d7a5b0e9a11" "Expected the user id"
      Expect.equal entity.RowKey (string device.Id) "Expected the endpoint hash"
    }

    test "a device survives the round trip through an entity" {
      let entity = (DbRegisteredDevice.FromDevice device).ToEntity()

      match DbRegisteredDevice.FromEntity entity |> Result.map _.ToDevice() with
      | Ok (Ok roundTripped) -> Expect.equal roundTripped device "Expected the same device"
      | other -> failtestf "Expected a device, got %A" other
    }

    test "a row with no auth is an error" {
      let entity = (DbRegisteredDevice.FromDevice device).ToEntity()
      entity.Remove "Auth" |> ignore
      Expect.isError (DbRegisteredDevice.FromEntity entity) "Expected an error"
    }
  ]
