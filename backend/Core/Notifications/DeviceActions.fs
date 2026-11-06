namespace TwoPoint.Core.Notifications.Actions

open TwoPoint.Core.Notifications
open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

open System

// ====================================================================================================================
// Register a device for push notifications
// ====================================================================================================================

[<RequireQualifiedAccess>]
module RegisterPushDevice =

  type Dto =
    { UserExternalId : string
      Endpoint : string
      P256dh : string
      Auth : string
      UserAgent : string }

  type Input =
    { UserId : UserExternalId
      Endpoint : PushEndpoint
      P256dh : NonEmptyString
      Auth : NonEmptyString
      Label : NonEmptyString }

  let create (dto : Dto) = validation {
    let! userId = UserExternalId.create dto.UserExternalId
    and! endpoint = PushEndpoint.create dto.Endpoint
    and! p256dh = NonEmptyString.create (Some "P256dh") dto.P256dh
    and! auth = NonEmptyString.create (Some "Auth") dto.Auth

    return
      { UserId = userId
        Endpoint = endpoint
        P256dh = p256dh
        Auth = auth
        Label = DeviceLabel.ofUserAgent dto.UserAgent }
  }

  // Output

  type Error = unit
  type Event = Registered of RegisteredDevice

  // Logic

  let decide (utcNow : DateTime) (existing : RegisteredDevice option) (input : Input) : RegisteredDevice =
    { UserId = input.UserId
      Id = DeviceId.ofEndpoint input.Endpoint
      Endpoint = input.Endpoint
      P256dh = input.P256dh
      Auth = input.Auth
      Label = input.Label
      CreatedDate = existing |> Option.map _.CreatedDate |> Option.defaultValue utcNow
      LastSeenDate = utcNow }

  // Action

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetDevice: UserExternalId -> DeviceId -> DependencyResult<RegisteredDevice option>
    abstract member GetUtcNow: unit -> DateTime
    abstract member SaveDevice: RegisteredDevice -> DependencyResult<RegisteredDevice>

  let withDependencies (dependencies : Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! input = dto |> create |> Result.mapError ActionError<Error>.Validation

      let! existing =
        dependencies.GetDevice input.UserId (DeviceId.ofEndpoint input.Endpoint)
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! saved =
        dependencies.SaveDevice (decide (dependencies.GetUtcNow()) existing input)
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      return Registered saved
    }

// ====================================================================================================================
// Remove a registered device
// ====================================================================================================================

[<RequireQualifiedAccess>]
module RemovePushDevice =

  type Dto =
    { UserExternalId : string
      DeviceId : string }

  // Output

  type Error = DeviceNotFound of DeviceId
  type Event = Removed of RegisteredDevice

  // Action

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetDevice: UserExternalId -> DeviceId -> DependencyResult<RegisteredDevice option>
    abstract member DeleteDevice: RegisteredDevice -> DependencyResult<unit>

  let withDependencies (dependencies : Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! userId, deviceId =
        validation {
          let! userId = UserExternalId.create dto.UserExternalId
          and! deviceId = DeviceId.create dto.DeviceId
          return userId, deviceId
        }
        |> Result.mapError ActionError<Error>.Validation

      let! existing =
        dependencies.GetDevice userId deviceId
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let! device =
        existing
        |> Result.requireSome (ActionError<Error>.Logic (DeviceNotFound deviceId))

      do!
        dependencies.DeleteDevice device
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      return Removed device
    }
