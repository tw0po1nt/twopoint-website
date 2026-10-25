module TwoPoint.Dependencies.Tests.NewCommentPushTests

open TwoPoint.Core.Notifications
open TwoPoint.Core.Notifications.Actions
open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Dependencies.Notifications

open Azure.Data.Tables
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
  let sender = WebPushSender(new HttpClient(new Answering(status)), vapid, NullLogger.Instance)
  WebPushConfigured sender

let private dependencies setup =
  NewCommentPushDependencies(setup, TableServiceClient "UseDevelopmentStorage=true", NullLogger.Instance)

let private send setup =
  ((dependencies setup).SendPush device (Text.Encoding.UTF8.GetBytes "{}") CancellationToken.None).Result

[<Tests>]
let tests =
  testList "NewCommentPushDependencies" [
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
  ]
