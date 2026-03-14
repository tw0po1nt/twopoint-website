namespace TwoPoint.Core.Notifications.Logic

open TwoPoint.Core.Posts
open TwoPoint.Core.Shared

open FsToolkit.ErrorHandling

// ========================================================================================
// TYPES
// ========================================================================================

// -----------------------------------------------------------------------------------------
// Register device
// -----------------------------------------------------------------------------------------

// Input

type NewDeviceRegistration =
  { UserExternalId : UserExternalId
    Client : Client
    Token : NonEmptyString }

// Output

type RegisterDeviceError = unit

type DeviceRegistered =
  | Created of NewDeviceRegistration
  | Updated of DeviceRegistration * NewDeviceRegistration
  
type RegisterDeviceDecision = Decision<DeviceRegistered, RegisterDeviceError>

/// <summary>
/// Logic for creating a device registration for sending notifications
/// </summary>
type RegisterDevice =
  DeviceRegistration option
    -> NewDeviceRegistration
    -> RegisterDeviceDecision
    
    
[<RequireQualifiedAccess>]
module Notifications =
  
  let registerDevice : RegisterDevice =
    let deviceRegistrationUpdated newRegistration existing = Updated (existing, newRegistration)
    
    fun existingDeviceRegistration newDeviceRegistration ->
      let createdOrUpdated =
        existingDeviceRegistration
        |> Option.map (deviceRegistrationUpdated newDeviceRegistration)
        |> Option.defaultValue (Created newDeviceRegistration)
      Ok createdOrUpdated
