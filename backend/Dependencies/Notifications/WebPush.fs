namespace TwoPoint.Dependencies.Notifications

open TwoPoint.Core.Util
open TwoPoint.Dependencies.Util

open FsToolkit.ErrorHandling
open IcedTasks
open Microsoft.Extensions.Logging

open System
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Security.Cryptography
open System.Text
open System.Text.Json

/// The public key is the 65-octet uncompressed P-256 point, and the private key is the 32-octet scalar.
type VapidKeys =
  { Subject : string
    PublicKey : byte array
    PrivateKey : byte array }

type PushSubscription =
  { Endpoint : Uri
    P256dh : byte array
    Auth : byte array }

type PushResult =
  | Sent
  | EndpointGone

module Base64Url =

  let encode (bytes : byte array) =
    Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')

  let decode (value : string) =
    let padded = value.Replace('-', '+').Replace('_', '/')
    Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='))

module Aes128Gcm =

  let private tagLength = 16

  let private derive (ikm : byte array) (salt : byte array) (info : string) (length : int) =
    HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, length, salt, Encoding.ASCII.GetBytes(info + "\000"))

  let encrypt (ikm : byte array) (salt : byte array) (recordSize : int) (keyId : byte array) (plaintext : byte array) =
    if plaintext.Length + 1 + tagLength > recordSize then
      invalidArg (nameof plaintext) $"The plaintext of {plaintext.Length} octets does not fit in a record of {recordSize} octets"

    let key = derive ikm salt "Content-Encoding: aes128gcm" 16
    let nonce = derive ikm salt "Content-Encoding: nonce" 12
    let record = Array.append plaintext [| 2uy |]
    let ciphertext = Array.zeroCreate record.Length
    let tag = Array.zeroCreate tagLength
    use aes = new AesGcm(key, tagLength)
    aes.Encrypt(nonce, record, ciphertext, tag)

    let size = BitConverter.GetBytes(uint32 recordSize) |> Array.rev
    Array.concat [ salt; size; [| byte keyId.Length |]; keyId; ciphertext; tag ]

module WebPushEncryption =

  /// A push service accepts a body of 4096 octets. The header takes 86 octets, and the record adds 17.
  let maxPlaintext = 3993

  let private recordSize = 4096

  let private uncompressed (key : ECDiffieHellman) =
    let point = key.ExportParameters(false).Q
    Array.concat [ [| 4uy |]; point.X; point.Y ]

  let private userAgentKey (uaPublic : byte array) =
    ECDiffieHellman.Create(
      ECParameters(Curve = ECCurve.NamedCurves.nistP256, Q = ECPoint(X = uaPublic[1..32], Y = uaPublic[33..64]))
    )

  let encryptWith (asKey : ECDiffieHellman) (salt : byte array) (uaPublic : byte array) (authSecret : byte array) (plaintext : byte array) =
    if plaintext.Length > maxPlaintext then
      invalidArg (nameof plaintext) $"A push message has {maxPlaintext} octets at most, and the plaintext has {plaintext.Length}"

    use uaKey = userAgentKey uaPublic
    let asPublic = uncompressed asKey
    let ecdhSecret = asKey.DeriveRawSecretAgreement uaKey.PublicKey
    let keyInfo = Array.concat [ Encoding.ASCII.GetBytes "WebPush: info\000"; uaPublic; asPublic ]
    let ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, ecdhSecret, 32, authSecret, keyInfo)
    Aes128Gcm.encrypt ikm salt recordSize asPublic plaintext

  let encrypt (uaPublic : byte array) (authSecret : byte array) (plaintext : byte array) =
    use asKey = ECDiffieHellman.Create ECCurve.NamedCurves.nistP256
    encryptWith asKey (RandomNumberGenerator.GetBytes 16) uaPublic authSecret plaintext

module Vapid =

  let private json (value : obj) = JsonSerializer.SerializeToUtf8Bytes value |> Base64Url.encode

  /// A push service refuses an expiry of 24 hours or more.
  let lifetime = TimeSpan.FromHours 12.

  let jwt (keys : VapidKeys) (audience : string) (now : DateTimeOffset) =
    let header = json {| typ = "JWT"; alg = "ES256" |}
    let claims = json {| aud = audience; exp = now.Add(lifetime).ToUnixTimeSeconds(); sub = keys.Subject |}
    let unsigned = $"{header}.{claims}"
    use key =
      ECDsa.Create(
        ECParameters(
          Curve = ECCurve.NamedCurves.nistP256,
          D = keys.PrivateKey,
          Q = ECPoint(X = keys.PublicKey[1..32], Y = keys.PublicKey[33..64])
        )
      )
    let signature =
      key.SignData(Encoding.ASCII.GetBytes unsigned, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
    $"{unsigned}.{Base64Url.encode signature}"

  let authorization (keys : VapidKeys) (audience : string) (now : DateTimeOffset) =
    AuthenticationHeaderValue("vapid", $"t={jwt keys audience now}, k={Base64Url.encode keys.PublicKey}")

type WebPushSender(httpClient : HttpClient, vapid : VapidKeys, logger : ILogger) =

  member _.Send (subscription : PushSubscription) (payload : byte array) (ttl : TimeSpan) : DependencyResult<PushResult> =
    runDependencyWithLogging Dependency.WebPush logger $"{nameof WebPushSender}.Send"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let audience = subscription.Endpoint.GetLeftPart UriPartial.Authority
      let request = new HttpRequestMessage(HttpMethod.Post, subscription.Endpoint)
      request.Headers.Authorization <- Vapid.authorization vapid audience DateTimeOffset.UtcNow
      request.Headers.Add("TTL", string (int ttl.TotalSeconds))
      request.Content <- new ByteArrayContent(WebPushEncryption.encrypt subscription.P256dh subscription.Auth payload)
      request.Content.Headers.ContentEncoding.Add "aes128gcm"
      request.Content.Headers.ContentType <- MediaTypeHeaderValue "application/octet-stream"
      use! response = httpClient.SendAsync(request, ct)

      match response.StatusCode with
      | HttpStatusCode.NotFound
      | HttpStatusCode.Gone -> return EndpointGone
      | _ when response.IsSuccessStatusCode -> return Sent
      | status ->
        let! body = response.Content.ReadAsStringAsync ct
        return failwith $"The push service answered {int status} to POST {subscription.Endpoint}: {body}"
    }
