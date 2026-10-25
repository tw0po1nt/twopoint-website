namespace TwoPoint.Dependencies.Notifications

open TwoPoint.Core.Notifications
open TwoPoint.Core.Notifications.Actions
open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Comms

open Azure.Data.Tables
open FsToolkit.ErrorHandling
open Microsoft.Extensions.Logging

open System

type NewCommentPushDependencies(
  setup : WebPushSetup,
  tableServiceClient : TableServiceClient,
  logger : ILogger
) =

  let commsDb = CommsDb.live tableServiceClient logger

  /// A push service keeps a message for this long when the device is offline.
  let timeToLive = TimeSpan.FromDays 1.

  let notConfigured =
    { Dependency = Dependency.WebPush
      Type = DependencyErrorType.Validation "The 'WebPush' setting is missing, so the backend cannot send a push" }

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

  /// One row in the `pushes` table for each send
  member _.RecordPush (device : RegisteredDevice) (commentId : CommentId) : DependencyResult<unit> = cancellableTaskResult {
    let push =
      { DbPush.Id = Guid.NewGuid().ToString() // todo: model as ISystemDependencies
        OperationId = commentId.ToString()
        CreatedDate = DateTime.UtcNow // todo: model as ISystemDependencies
        Recipient = device.Id.ToString() }

    let! (_ : DbPush) = commsDb.CreatePush push
    return ()
  }
