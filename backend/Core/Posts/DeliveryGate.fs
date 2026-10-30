namespace TwoPoint.Core.Posts

open TwoPoint.Core.Deploys

open System

type NewPostDeliveries =
  | NoNewPostDelivery
  | HasNewPostDelivery

/// A new-post delivery waits until the site shows the text that the email contains.
[<RequireQualifiedAccess>]
module DeliveryGate =

  type Refusal =
    | NotYetPublished of publishDate : DateTime
    | LatestChangeDeploying
    | LatestChangeNotDeployed

  let check (utcNow : DateTime) (publishDate : DateTime) (deploy : DeployState) : Result<unit, Refusal> =
    if publishDate > utcNow then
      Error (NotYetPublished publishDate)
    else
      match deploy with
      | Deployed -> Ok ()
      | Deploying -> Error LatestChangeDeploying
      | DeployFailed -> Error LatestChangeNotDeployed

  /// A published post that passes the check is a delivery to start until it has a new-post delivery.
  let isToStart
    (utcNow : DateTime)
    (publishDate : DateTime)
    (deploy : DeployState)
    (mark : MigratedMark)
    (deliveries : NewPostDeliveries)
    : bool =
    match mark, deliveries with
    | Migrated, _
    | _, HasNewPostDelivery -> false
    | NotMigrated, NoNewPostDelivery -> check utcNow publishDate deploy |> Result.isOk
