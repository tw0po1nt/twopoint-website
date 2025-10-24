namespace TwoPoint.Core.Notifications.Dependencies

open System
open FsToolkit.ErrorHandling
open TwoPoint.Core.Notifications.Logic
open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

type INotificationDependencies =
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
      
[<RequireQualifiedAccess>]
module NotificationDependencies =
  
  let live tableServiceClient logger =
    
    let notificationDb = NotificationDb.live tableServiceClient logger
    
    let source = Dependency.Database
    
    { new INotificationDependencies with
      
      member this.GetDeviceRegistrationByUserAndClient userExternalId client = cancellableTaskResult {
        let userExternalId = userExternalId.ToString()
        let client = client.ToString()
        let! dbDeviceRegistration = notificationDb.GetDeviceRegistrationByUserAndClient userExternalId client
        return!
          dbDeviceRegistration
          |> Option.traverseResult (fun (dbo: DbDeviceRegistration) -> dbo.ToDeviceRegistration())
          |> DependencyError.ofValidation source
      }
      
      member this.CreateDeviceRegistration newDeviceRegistration = cancellableTaskResult {
        let newRegistration =
          { DbDeviceRegistration.Id = Guid.NewGuid().ToString() // todo: model as ISystemDependencies
            UserExternalId = newDeviceRegistration.UserExternalId.ToString()
            Client = newDeviceRegistration.Client.ToString()
            Token = newDeviceRegistration.Token.ToString()
            CreatedDate = DateTime.UtcNow } // todo: model as ISystemDependencies
        let! (dbDeviceRegistration: DbDeviceRegistration) = notificationDb.CreateDeviceRegistration newRegistration
        return! dbDeviceRegistration.ToDeviceRegistration() |> DependencyError.ofValidation source
      }
      
      member this.UpdateDeviceRegistration registration token = cancellableTaskResult {
        let dbDeviceRegistration =
          { DbDeviceRegistration.Id = registration.Id.ToString()
            UserExternalId = registration.UserExternalId.ToString()
            Client = registration.Client.ToString()
            Token = registration.Token.ToString()
            CreatedDate = registration.CreatedDate }
        let dbToken = token.ToString()
        let! (dbDeviceRegistration: DbDeviceRegistration) = notificationDb.UpdateDeviceRegistration dbDeviceRegistration dbToken
        return! dbDeviceRegistration.ToDeviceRegistration() |> DependencyError.ofValidation source
      }
    }
