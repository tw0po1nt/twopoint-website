namespace TwoPoint.Core.Notifications.Api

open TwoPoint.Core.Notifications.Dependencies
open TwoPoint.Core.Notifications.Logic
open TwoPoint.Core.Posts
open TwoPoint.Core.Util

open IcedTasks

// Inputs
type NewDeviceRegistrationDto =
  { UserExternalId : string
    Client : string
    Token : string }
  
// Outputs
type RegistrationType =
  | Created
  | Updated

type DeviceRegisteredEvent =
  { Registration : DeviceRegistration
    Type : RegistrationType }

// Actions

type INotificationActions =
  abstract member RegisterDevice: newDeviceRegistration: NewDeviceRegistrationDto -> CancellableTask<ActionResult<DeviceRegisteredEvent, RegisterDeviceError>>

[<RequireQualifiedAccess>]
module NotificationActions =
  
  open FsToolkit.ErrorHandling
  
  let withDependencies (notificationDependencies: INotificationDependencies) =
    
    let handleDeviceRegistered (deviceRegistered : DeviceRegistered) : DependencyResult<DeviceRegisteredEvent> =
      match deviceRegistered with
      | DeviceRegistered.Created newRegistration ->
        notificationDependencies.CreateDeviceRegistration newRegistration
        |> CancellableTaskResult.map (fun registration -> { Registration = registration; Type = Created })
      | DeviceRegistered.Updated (existing, updates) ->
        notificationDependencies.UpdateDeviceRegistration existing updates.Token
        |> CancellableTaskResult.map (fun registration -> { Registration = registration; Type = Updated })
    
    { new INotificationActions with
    
      member this.RegisterDevice newDeviceRegistration = cancellableTaskResult {
        let! (newDeviceRegistration: NewDeviceRegistration) =
          NewDeviceRegistration.create
            newDeviceRegistration.UserExternalId
            newDeviceRegistration.Client
            newDeviceRegistration.Token
          |> Result.mapError ActionError<RegisterDeviceError>.Validation
                      
        let! existingRegistration =
          notificationDependencies.GetDeviceRegistrationByUserAndClient newDeviceRegistration.UserExternalId newDeviceRegistration.Client
          |> CancellableTaskResult.mapError ActionError<RegisterDeviceError>.Dependency
          
        let decision = Notifications.registerDevice existingRegistration newDeviceRegistration
          
        return!
          decision
          |> Result.traverseCancellableTask handleDeviceRegistered
          |> CancellableTaskResult.foldResult
            (DependencyResult.toActionResult id)
            (ActionError.Logic >> ActionResult.failure)
      }
      
    }
  
  
