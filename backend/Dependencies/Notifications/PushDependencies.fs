namespace TwoPoint.Dependencies.Notifications

open TwoPoint.Core.Deploys
open TwoPoint.Core.Notifications
open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Comms
open TwoPoint.Dependencies.Deploys
open TwoPoint.Dependencies.Util

open Azure.Data.Tables
open FsToolkit.ErrorHandling
open Microsoft.Extensions.Logging

open System

type PushDependencies internal (
  setup : WebPushSetup,
  commsDb : ICommsDb,
  system : ISystemDependencies
) =

  /// A push service keeps a message for this long when the device is offline.
  let timeToLive = TimeSpan.FromDays 1.

  let notConfigured =
    { Dependency = Dependency.WebPush
      Type = DependencyErrorType.Validation "The 'WebPush' setting is missing, so the backend cannot send a push" }

  let record (device : RegisteredDevice) (operationId : string) : DependencyResult<unit> = cancellableTaskResult {
    let push =
      { DbPush.Id = system.NewGuid().ToString()
        OperationId = operationId
        CreatedDate = system.GetUtcNow()
        Recipient = device.Id.ToString() }

    let! (_ : DbPush) = commsDb.CreatePush push
    return ()
  }

  member _.SendPush (device : RegisteredDevice) (payload : byte array) : DependencyResult<PushOutcome> =
    match setup with
    | WebPushNotConfigured -> CancellableTaskResult.error notConfigured
    | WebPushConfigured sender ->
      let subscription =
        { PushSubscription.Endpoint = Uri(device.Endpoint.ToString())
          P256dh = device.P256dh |> NonEmptyString.value |> Base64Url.decode
          Auth = device.Auth |> NonEmptyString.value |> Base64Url.decode }

      sender.Send subscription payload timeToLive
      |> CancellableTaskResult.map (function
        | Sent -> Delivered
        | EndpointGone -> DeviceGone)

  member _.RecordCommentPush (device : RegisteredDevice) (commentId : CommentId) = record device (commentId.ToString())

  member _.RecordRunPush (device : RegisteredDevice) (run : SiteRun) =
    record device $"site-run:{DeployRows.runPartition run.Repo}:{run.Number.Value}:{run.Attempt.Value}"

  new(setup : WebPushSetup, tableServiceClient : TableServiceClient, system : ISystemDependencies, logger : ILogger) =
    PushDependencies(setup, CommsDb.live tableServiceClient logger, system)
