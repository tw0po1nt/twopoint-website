module TwoPoint.Dependencies.Tests.PushTests

open TwoPoint.Core.Notifications
open TwoPoint.Core.Notifications.Actions
open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Dependencies.Comms
open TwoPoint.Dependencies.Notifications
open TwoPoint.Dependencies.Tests.Fakes
open TwoPoint.Dependencies.Util

open Expecto
open Microsoft.Extensions.Logging.Abstractions

open System
open System.Net
open System.Net.Http
open System.Security.Cryptography
open System.Threading

type private Answering(status : HttpStatusCode) =
  inherit HttpMessageHandler()

  override _.SendAsync(_, _) = Threading.Tasks.Task.FromResult(new HttpResponseMessage(status))

let private device =
  let at = DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc)
  let ok result = result |> Result.defaultWith (fun error -> failwithf "%A" error)
  let endpoint = PushEndpoint.create "https://push.example.net/send/abc" |> ok
  { RegisteredDevice.UserId = UserExternalId.create "6f1c1b5e-3d2a-4a43-9c6b-0d7a5b0e9a11" |> ok
    Id = DeviceId.ofEndpoint endpoint
    Endpoint = endpoint
    P256dh =
      NonEmptyString.create None "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4" |> ok
    Auth = NonEmptyString.create None "BTBZMqHH6r4Tts7J_aSIgg" |> ok
    Label = NonEmptyString.create None "Safari on macOS" |> ok
    CreatedDate = at
    LastSeenDate = at }

let private configured (status : HttpStatusCode) =
  let key = ECDsa.Create ECCurve.NamedCurves.nistP256
  let parameters = key.ExportParameters true
  let vapid =
    { VapidKeys.Subject = "mailto:matt@twopoint.dev"
      PublicKey = Array.concat [ [| 4uy |]; parameters.Q.X; parameters.Q.Y ]
      PrivateKey = parameters.D }
  let sender = WebPushSender(new HttpClient(new Answering(status)), vapid, SystemDependencies.live, NullLogger.Instance)
  WebPushConfigured sender

let private now = DateTime(2026, 11, 8, 9, 30, 0, DateTimeKind.Utc)
let private pushGuid = Guid "2c1b0a9f-8e7d-4c6b-9a5f-4e3d2c1b0a9f"

let private dependencies setup =
  PushDependencies(setup, RecordingCommsDb(), FixedSystem([], now))

let private send setup =
  ((dependencies setup).SendPush device (Text.Encoding.UTF8.GetBytes "{}") CancellationToken.None).Result

[<Tests>]
let tests =
  testList "PushDependencies" [
    test "a 201 from the push service is a delivery" {
      Expect.equal (send (configured HttpStatusCode.Created)) (Ok Delivered) "Expected Delivered"
    }

    test "a 410 from the push service is a gone device" {
      Expect.equal (send (configured HttpStatusCode.Gone)) (Ok DeviceGone) "Expected DeviceGone"
    }

    test "a backend with no WebPush setting cannot send" {
      match send WebPushNotConfigured with
      | Error _ -> ()
      | other -> failtestf "Expected an error, got %A" other
    }

    test "the push row of a comment gets a new ID, the comment ID, the time of the clock, and the device" {
      let commsDb = RecordingCommsDb()
      let push = PushDependencies(WebPushNotConfigured, commsDb, FixedSystem([ pushGuid ], now))
      let commentId = CommentId.ofGuid (Guid "8f6c1b2e-0d4a-4c3e-9b1a-2f5d6e7a8b9c")

      let recorded = (push.RecordCommentPush device commentId CancellationToken.None).Result

      Expect.isOk recorded "Expected the push row"
      Expect.equal
        commsDb.Pushes
        [ { DbPush.Id = string pushGuid
            OperationId = string commentId
            CreatedDate = now
            Recipient = string device.Id } ]
        "Expected the push row"
    }
  ]
