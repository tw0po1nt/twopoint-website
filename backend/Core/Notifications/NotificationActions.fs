namespace TwoPoint.Core.Notifications.Actions

open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

// ====================================================================================================================
// Register device
// ====================================================================================================================

// Input

type NewDeviceRegistration =
  { UserExternalId : UserExternalId
    Client : Client
    Token : NonEmptyString }

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

[<RequireQualifiedAccess>]
module RegisterDevice =
  
  // Output

  type Error = unit
  type Success =
    | Created of NewDeviceRegistration
    | Updated of DeviceRegistration * NewDeviceRegistration
  type Decision = Decision<Success, Error>
  type Event = Registered of RegistrationData
  and RegistrationData =
    { Registration : DeviceRegistration
      Type : RegistrationType }
  and RegistrationType =
    | Created
    | Updated

  // Logic

  type Logic =
    DeviceRegistration option
      -> NewDeviceRegistration
      -> Decision
    
  [<RequireQualifiedAccess>]
  module Logic =
    let decide : Logic =
      let deviceRegistrationUpdated newRegistration existing = Success.Updated (existing, newRegistration)

      fun existingDeviceRegistration newDeviceRegistration ->
        let createdOrUpdated =
          existingDeviceRegistration
          |> Option.map (deviceRegistrationUpdated newDeviceRegistration)
          |> Option.defaultValue (Success.Created newDeviceRegistration)
        Ok createdOrUpdated
  
  // Action

  type Action =
    NewDeviceRegistration.Dto
      -> CancellableTask<ActionResult<Event, Error>>

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

  let withDependencies (dependencies: Dependencies) : Action =

    let handleDeviceRegistered (deviceRegistered : Success) : DependencyResult<Event> =
      match deviceRegistered with
      | Success.Created newRegistration ->
        dependencies.CreateDeviceRegistration newRegistration
        |> CancellableTaskResult.map (fun registration -> Registered { Registration = registration; Type = Created })
      | Success.Updated (existing, updates) ->
        dependencies.UpdateDeviceRegistration existing updates.Token
        |> CancellableTaskResult.map (fun registration -> Registered { Registration = registration; Type = Updated })

    fun newDeviceRegistration -> cancellableTaskResult {
      let! (newDeviceRegistration: NewDeviceRegistration) =
        newDeviceRegistration
        |> NewDeviceRegistration.create
        |> Result.mapError ActionError<Error>.Validation

      let! existingRegistration =
        dependencies.GetDeviceRegistrationByUserAndClient newDeviceRegistration.UserExternalId newDeviceRegistration.Client
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      let decision = Logic.decide existingRegistration newDeviceRegistration

      return!
        decision
        |> Result.traverseCancellableTask handleDeviceRegistered
        |> CancellableTaskResult.foldResult
          (DependencyResult.toActionResult id)
          (ActionError.Logic >> ActionResult.failure)
    }
