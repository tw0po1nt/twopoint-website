namespace TwoPoint.Dependencies.Notifications

open TwoPoint.Core.Notifications.Logic
open TwoPoint.Core.Posts
open TwoPoint.Core.Util

open Azure.Data.Tables
open FsToolkit.ErrorHandling
open Microsoft.Extensions.Logging

open System

type NotificationDependencies(
  tableServiceClient : TableServiceClient,
  logger : ILogger
) =

  let notificationDb = NotificationDb.live tableServiceClient logger

  let source = Dependency.Database

  member _.GetDeviceRegistrationByUserAndClient userExternalId client = cancellableTaskResult {
    let userExternalId = userExternalId.ToString()
    let client = client.ToString()
    let! dbDeviceRegistration = notificationDb.GetDeviceRegistrationByUserAndClient userExternalId client
    return!
      dbDeviceRegistration
      |> Option.traverseResult (fun (dbo: DbDeviceRegistration) -> dbo.ToDeviceRegistration())
      |> DependencyError.ofValidation source
  }

  member _.CreateDeviceRegistration (newDeviceRegistration : NewDeviceRegistration) = cancellableTaskResult {
    let newRegistration =
      { DbDeviceRegistration.Id = Guid.NewGuid().ToString() // todo: model as ISystemDependencies
        UserExternalId = newDeviceRegistration.UserExternalId.ToString()
        Client = newDeviceRegistration.Client.ToString()
        Token = newDeviceRegistration.Token.ToString()
        CreatedDate = DateTime.UtcNow } // todo: model as ISystemDependencies
    let! (dbDeviceRegistration: DbDeviceRegistration) = notificationDb.CreateDeviceRegistration newRegistration
    return! dbDeviceRegistration.ToDeviceRegistration() |> DependencyError.ofValidation source
  }

  member _.UpdateDeviceRegistration registration token = cancellableTaskResult {
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
