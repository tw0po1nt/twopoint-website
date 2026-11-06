module TwoPoint.Core.Tests.AccessTests

open TwoPoint.Core.Shared

open Expecto

let private webClientId =
  Access.clients |> Map.findKey (fun _ name -> name = "web")

let private author : AccessClaims =
  { ObjectId = Some Access.authorObjectId
    Scope = Some Access.scope
    AuthorizedParty = Some webClientId }

[<Tests>]
let tests =
  testList "Access.decide" [
    test "the author through the web client gets access" {
      Expect.equal (Access.decide author) (Allowed "web") "Expected access for the web client"
    }

    test "the map of clients has only the web client" {
      Expect.equal (Access.clients |> Map.values |> List.ofSeq) [ "web" ] "Expected one client"
    }

    test "a token from another client is forbidden" {
      let claims = { author with AuthorizedParty = Some "e0505a28-fb17-42d3-8bbd-e98bf44f089e" }
      Expect.equal (Access.decide claims) (Forbidden UnknownClient) "Expected a 403 for another azp"
    }

    test "a token with no azp is forbidden" {
      let claims = { author with AuthorizedParty = None }
      Expect.equal (Access.decide claims) (Forbidden UnknownClient) "Expected a 403 for a v1.0 token"
    }

    test "a token for another user is forbidden" {
      let claims = { author with ObjectId = Some "3f6c9e1a-2b7d-4c58-9e0f-1a2b3c4d5e6f" }
      Expect.equal (Access.decide claims) (Forbidden UnknownUser) "Expected a 403 for another oid"
    }

    test "a token with no user is forbidden" {
      let claims = { author with ObjectId = None }
      Expect.equal (Access.decide claims) (Forbidden UnknownUser) "Expected a 403 with no oid"
    }

    test "a token with another scope is forbidden" {
      let claims = { author with Scope = Some "User.Read" }
      Expect.equal (Access.decide claims) (Forbidden UnknownScope) "Expected a 403 for another scp"
    }

    test "a token with more than the one scope is forbidden" {
      let claims = { author with Scope = Some "access_as_user User.Read" }
      Expect.equal (Access.decide claims) (Forbidden UnknownScope) "Expected a 403 when scp is not access_as_user"
    }
  ]
