namespace TwoPoint.Core.Posts

open TwoPoint.Core.Deploys

open System

/// A new-post delivery waits until the site shows the text that the email contains.
[<RequireQualifiedAccess>]
module DeliveryGate =

  type Refusal =
    | NotYetPublished of publishDate : DateTime
    | LatestChangeDeploying
    | LatestDeployFailed

  let check (utcNow : DateTime) (publishDate : DateTime) (deploy : DeployState) : Result<unit, Refusal> =
    if publishDate > utcNow then
      Error (NotYetPublished publishDate)
    else
      match deploy with
      | Deployed -> Ok ()
      | Deploying -> Error LatestChangeDeploying
      | DeployFailed -> Error LatestDeployFailed

  let checkPost (utcNow : DateTime) (deploys : DeployContext) (post : Post) : Result<unit, Refusal> =
    DeployRule.stateOf utcNow deploys post.Id |> check utcNow post.Info.CreatedDate

  let isDeliveryToStart
    (utcNow : DateTime)
    (publishDate : DateTime)
    (deploy : DeployState)
    (mark : MigratedMark)
    (start : DeliveryStart)
    : bool =
    match mark, start with
    | Migrated, _
    | _, Started _ -> false
    | NotMigrated, NotStarted -> check utcNow publishDate deploy |> Result.isOk
