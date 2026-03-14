namespace TwoPoint.Core.Notifications.Actions

open TwoPoint.Core.Notifications.Logic
open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

// ====================================================================================================================
// Register device
// ====================================================================================================================

// Input

module NewDeviceRegistration =

  type UserExternalId = UserExternalId of string
  type Client = Client of string
  type Token = Token of string

  type Dto =
    { UserExternalId : UserExternalId 
      Client : Client
      Token : Token }

  let create (dto : Dto) = validation {
    let (UserExternalId userExternalId) = dto.UserExternalId
    let (Client client) = dto.Client
    let (Token token) = dto.Token
    let! userExternalId = UserExternalId.create userExternalId
    and! client = Client.create client
    and! token = NonEmptyString.create (Some (nameof token)) token

    return
      { NewDeviceRegistration.UserExternalId = userExternalId
        Client = client
        Token = token }
  }

// Output

type RegistrationType =
  | Created
  | Updated

type DeviceRegisteredEvent =
  { Registration : DeviceRegistration
    Type : RegistrationType }

// Action

type RegisterDevice =
  NewDeviceRegistration.Dto
    -> CancellableTask<ActionResult<DeviceRegisteredEvent, RegisterDeviceError>>

[<RequireQualifiedAccess>]
module RegisterDevice =

  type Dependencies =
    abstract member GetDeviceRegistrationByUserAndClient:
      userExternalId: UserExternalId
        -> client: Client
        -> DependencyResult<DeviceRegistration option>
    abstract member CreateDeviceRegistration:
      newDeviceRegistration: NewDeviceRegistration
        -> DependencyResult<DeviceRegistration>
    abstract member UpdateDeviceRegistration:
      registration: DeviceRegistration
        -> token: NonEmptyString
        -> DependencyResult<DeviceRegistration>

  let withDependencies (dependencies: Dependencies) : RegisterDevice =

    let handleDeviceRegistered (deviceRegistered : DeviceRegistered) : DependencyResult<DeviceRegisteredEvent> =
      match deviceRegistered with
      | DeviceRegistered.Created newRegistration ->
        dependencies.CreateDeviceRegistration newRegistration
        |> CancellableTaskResult.map (fun registration -> { Registration = registration; Type = Created })
      | DeviceRegistered.Updated (existing, updates) ->
        dependencies.UpdateDeviceRegistration existing updates.Token
        |> CancellableTaskResult.map (fun registration -> { Registration = registration; Type = Updated })


    fun newDeviceRegistration -> cancellableTaskResult {
      let! (newDeviceRegistration: NewDeviceRegistration) =
        newDeviceRegistration
        |> NewDeviceRegistration.create
        |> Result.mapError ActionError<RegisterDeviceError>.Validation

      let! existingRegistration =
        dependencies.GetDeviceRegistrationByUserAndClient newDeviceRegistration.UserExternalId newDeviceRegistration.Client
        |> CancellableTaskResult.mapError ActionError<RegisterDeviceError>.Dependency

      let decision = Notifications.registerDevice existingRegistration newDeviceRegistration

      return!
        decision
        |> Result.traverseCancellableTask handleDeviceRegistered
        |> CancellableTaskResult.foldResult
          (DependencyResult.toActionResult id)
          (ActionError.Logic >> ActionResult.failure)
    }
