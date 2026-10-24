module TwoPoint.Dependencies.Tests.WebPushTests

open TwoPoint.Core.Util
open TwoPoint.Dependencies.Notifications

open Expecto
open Microsoft.Extensions.Logging.Abstractions

open System
open System.Net
open System.Net.Http
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Threading

let private b64 (value : string) =
  let padded = value.Replace('-', '+').Replace('_', '/')
  Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='))

let private text (value : string) = Encoding.UTF8.GetBytes value

let private ecdhKey (privateKey : string) (publicKey : string) =
  let point = b64 publicKey
  ECDiffieHellman.Create(
    ECParameters(
      Curve = ECCurve.NamedCurves.nistP256,
      D = b64 privateKey,
      Q = ECPoint(X = point[1..32], Y = point[33..64])
    )
  )

let private asPublic = "BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8"
let private asPrivate = "yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw"
let private uaPublic = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4"
let private authSecret = "BTBZMqHH6r4Tts7J_aSIgg"
let private salt = "DGv6ra1nlYgDCS1FRnbzlw"
let private plaintext = "V2hlbiBJIGdyb3cgdXAsIEkgd2FudCB0byBiZSBhIHdhdGVybWVsb24"

let private rfc8291Header =
  "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8"

let private rfc8291Ciphertext =
  "8pfeW0KbunFT06SuDKoJH9Ql87S1QUrdirN6GcG7sFz1y1sqLgVi1VhjVkHsUoEsbI_0LpXMuGvnzQ"

let private p256Vapid () =
  let key = ECDsa.Create ECCurve.NamedCurves.nistP256
  let parameters = key.ExportParameters true
  let publicKey = Array.concat [ [| 4uy |]; parameters.Q.X; parameters.Q.Y ]
  key, { VapidKeys.Subject = "mailto:matt@twopoint.dev"; PublicKey = publicKey; PrivateKey = parameters.D }

let private decode (part : string) = part |> b64 |> Encoding.UTF8.GetString |> JsonDocument.Parse |> _.RootElement

let private now = DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero)

type private FakePushService(status : HttpStatusCode) =
  inherit HttpMessageHandler()
  let mutable captured : HttpRequestMessage option = None
  let mutable body : byte array = [||]

  member _.Request = captured.Value
  member _.Body = body

  override _.SendAsync(request, ct) =
    captured <- Some request
    body <- request.Content.ReadAsByteArrayAsync(ct).Result
    Threading.Tasks.Task.FromResult(new HttpResponseMessage(status))

let private subscription : PushSubscription =
  { Endpoint = Uri "https://push.example.net/send/abc"
    P256dh = b64 uaPublic
    Auth = b64 authSecret }

let private send (status : HttpStatusCode) =
  let _, vapid = p256Vapid ()
  let service = new FakePushService(status)
  let sender = WebPushSender(new HttpClient(service), vapid, NullLogger.Instance)
  let result = (sender.Send subscription (text "{}") (TimeSpan.FromHours 1.) CancellationToken.None).Result
  service, result

[<Tests>]
let tests =
  testList "WebPush" [
    test "Aes128Gcm encrypts the example of RFC 8188" {
      let message =
        Aes128Gcm.encrypt (b64 "yqdlZ-tYemfogSmv7Ws5PQ") (b64 "I1BsxtFttlv3u_Oo94xnmw") 4096 [||] (text "I am the walrus")
      let expected = "I1BsxtFttlv3u_Oo94xnmwAAEAAA-NAVub2qFgBEuQKRapoZu-IxkIva3MEB1PD-ly8Thjg"
      Expect.equal message (b64 expected) "Expected the 54 octets of RFC 8188 section 3.1"
    }

    test "Aes128Gcm refuses a plaintext that does not fit in one record" {
      Expect.throws
        (fun () -> Aes128Gcm.encrypt (Array.zeroCreate 16) (Array.zeroCreate 16) 32 [||] (Array.zeroCreate 16) |> ignore)
        "Expected a refusal"
    }

    test "WebPushEncryption encrypts the example of RFC 8291 appendix A" {
      use key = ecdhKey asPrivate asPublic
      let message = WebPushEncryption.encryptWith key (b64 salt) (b64 uaPublic) (b64 authSecret) (b64 plaintext)
      Expect.equal message (Array.append (b64 rfc8291Header) (b64 rfc8291Ciphertext)) "Expected the message of RFC 8291 appendix A"
    }

    test "WebPushEncryption uses a new key and a new salt for each message" {
      let first = WebPushEncryption.encrypt (b64 uaPublic) (b64 authSecret) (text "a")
      let second = WebPushEncryption.encrypt (b64 uaPublic) (b64 authSecret) (text "a")
      Expect.notEqual first second "Expected different messages"
    }

    test "WebPushEncryption refuses a plaintext of more than 3993 octets" {
      Expect.throws
        (fun () -> WebPushEncryption.encrypt (b64 uaPublic) (b64 authSecret) (Array.zeroCreate 3994) |> ignore)
        "Expected a refusal"
      Expect.equal (WebPushEncryption.encrypt (b64 uaPublic) (b64 authSecret) (Array.zeroCreate 3993)).Length 4096 "Expected a full record"
    }

    test "the VAPID JWT has the ES256 header" {
      let _, vapid = p256Vapid ()
      let header = (Vapid.jwt vapid "https://push.example.net" now).Split('.')[0] |> decode
      Expect.equal (header.GetProperty("typ").GetString()) "JWT" "Expected a JWT"
      Expect.equal (header.GetProperty("alg").GetString()) "ES256" "Expected ES256"
    }

    test "the VAPID JWT names the push service, ends within one day, and names a mailto subject" {
      let _, vapid = p256Vapid ()
      let claims = (Vapid.jwt vapid "https://push.example.net" now).Split('.')[1] |> decode
      Expect.equal (claims.GetProperty("aud").GetString()) "https://push.example.net" "Expected the origin"
      Expect.equal (claims.GetProperty("sub").GetString()) "mailto:matt@twopoint.dev" "Expected the subject"
      let exp = claims.GetProperty("exp").GetInt64()
      Expect.isGreaterThan exp (now.ToUnixTimeSeconds()) "Expected a future expiry"
      Expect.isLessThan exp (now.AddDays(1.).ToUnixTimeSeconds()) "Expected an expiry of less than one day"
    }

    test "the VAPID JWT signature is 64 octets and verifies with the public key" {
      let key, vapid = p256Vapid ()
      let jwt = Vapid.jwt vapid "https://push.example.net" now
      let parts = jwt.Split('.')
      let signature = b64 parts[2]
      Expect.equal signature.Length 64 "Expected r and s of 32 octets each"
      let signed = text $"{parts[0]}.{parts[1]}"
      Expect.isTrue
        (key.VerifyData(signed, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
        "Expected a valid ES256 signature"
    }

    test "Send posts the encrypted message with the TTL and the VAPID header" {
      let service, result = send HttpStatusCode.Created
      Expect.equal result (Ok PushResult.Sent) "Expected Sent"
      let request = service.Request
      Expect.equal request.Method HttpMethod.Post "Expected a POST"
      Expect.equal (request.Headers.GetValues "TTL" |> Seq.toList) [ "3600" ] "Expected the TTL"
      Expect.equal (request.Content.Headers.ContentEncoding |> Seq.toList) [ "aes128gcm" ] "Expected aes128gcm"
      let authorization = request.Headers.Authorization
      Expect.equal authorization.Scheme "vapid" "Expected the vapid scheme"
      Expect.stringContains authorization.Parameter "t=" "Expected the JWT"
      Expect.stringContains authorization.Parameter ", k=" "Expected the public key"
      Expect.equal service.Body.Length (86 + 2 + 1 + 16) "Expected a header and a record of the payload"
    }

    test "Send reports 404 and 410 as a gone endpoint" {
      for status in [ HttpStatusCode.NotFound; HttpStatusCode.Gone ] do
        let _, result = send status
        Expect.equal result (Ok PushResult.EndpointGone) $"Expected EndpointGone for {int status}"
    }

    test "Send reports another failure as a dependency error" {
      let _, result = send HttpStatusCode.InternalServerError
      match result with
      | Error error -> Expect.equal error.Dependency Dependency.WebPush "Expected the push service"
      | Ok _ -> failtest "Expected an error"
    }
  ]
