namespace TwoPoint.Core.Shared

/// The claims of a v2.0 access token that decide if a request can use an admin endpoint.
type AccessClaims =
  { ObjectId : string option
    Scope : string option
    AuthorizedParty : string option }

type ForbiddenReason =
  | UnknownUser
  | UnknownScope
  | UnknownClient

type Access =
  | Allowed of client : string
  | Forbidden of ForbiddenReason

[<RequireQualifiedAccess>]
module Access =

  let authorObjectId = "ad1209ae-c4cc-4360-953a-ccbab9d68c83"

  let scope = "access_as_user"

  /// The client ID of each Entra registration that can call the admin endpoints, and its client name.
  let clients = Map [ "25b7422d-9fb9-41a3-a7f0-27287c2b89e9", "web" ]

  let decide (claims : AccessClaims) : Access =
    if claims.ObjectId <> Some authorObjectId then
      Forbidden UnknownUser
    elif claims.Scope <> Some scope then
      Forbidden UnknownScope
    else
      match claims.AuthorizedParty |> Option.bind (fun id -> Map.tryFind id clients) with
      | Some client -> Allowed client
      | None -> Forbidden UnknownClient
