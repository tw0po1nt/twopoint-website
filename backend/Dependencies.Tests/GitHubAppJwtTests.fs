module TwoPoint.Dependencies.Tests.GitHubAppJwtTests

open TwoPoint.Dependencies.Deploys

open Expecto

open System
open System.Text
open System.Text.Json

let private decode (part : string) =
  let padded = part.Replace('-', '+').Replace('_', '/').PadRight(part.Length + (4 - part.Length % 4) % 4, '=')
  padded |> Convert.FromBase64String |> Encoding.UTF8.GetString |> JsonDocument.Parse |> _.RootElement

let private now = DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero)

[<Tests>]
let tests =
  testList "GitHubAppJwt" [
    test "the header names RS256" {
      let header = (GitHubAppJwt.unsigned "Iv23client" now).Split('.')[0] |> decode
      Expect.equal (header.GetProperty("alg").GetString()) "RS256" "Expected RS256"
      Expect.equal (header.GetProperty("typ").GetString()) "JWT" "Expected a JWT"
    }

    test "the payload names the app, starts 60 seconds early, and ends within 10 minutes" {
      let payload = (GitHubAppJwt.unsigned "Iv23client" now).Split('.')[1] |> decode
      Expect.equal (payload.GetProperty("iss").GetString()) "Iv23client" "Expected the client ID"
      Expect.equal (payload.GetProperty("iat").GetInt64()) (now.ToUnixTimeSeconds() - 60L) "Expected 60 seconds early"
      Expect.equal (payload.GetProperty("exp").GetInt64()) (now.ToUnixTimeSeconds() + 540L) "Expected 9 minutes later"
    }

    test "the signature is the third part, in base64url with no padding" {
      let jwt = GitHubAppJwt.complete "header.payload" [| 0xfbuy; 0xffuy; 0xfeuy; 0x01uy |]
      Expect.equal jwt "header.payload.-__-AQ" "Expected base64url"
    }
  ]
