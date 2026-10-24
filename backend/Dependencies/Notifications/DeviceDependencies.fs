namespace TwoPoint.Dependencies.Notifications

open TwoPoint.Core.Notifications
open TwoPoint.Core.Posts
open TwoPoint.Core.Util

open Azure.Data.Tables
open FsToolkit.ErrorHandling
open Microsoft.Extensions.Logging

open System

type DeviceDependencies(
  tableServiceClient : TableServiceClient,
  logger : ILogger
) =

  let deviceDb = DeviceDb.live tableServiceClient logger

  let source = Dependency.Database

  member _.GetDevicesOfUser (userId : UserExternalId) = cancellableTaskResult {
    let! (dbDevices : DbRegisteredDevice list) = deviceDb.GetDevicesOfUser (userId.ToString())

    return!
      dbDevices
      |> List.traverseValidationA _.ToDevice()
      |> DependencyError.ofValidation source
  }

  member _.GetDevice (userId : UserExternalId) (deviceId : DeviceId) = cancellableTaskResult {
    let! dbDevice = deviceDb.GetDevice (userId.ToString()) (deviceId.ToString())

    return!
      dbDevice
      |> Option.traverseResult (fun (dbo : DbRegisteredDevice) -> dbo.ToDevice())
      |> DependencyError.ofValidation source
  }

  member _.SaveDevice (device : RegisteredDevice) = cancellableTaskResult {
    let! (dbDevice : DbRegisteredDevice) = deviceDb.SaveDevice (DbRegisteredDevice.FromDevice device)
    return! dbDevice.ToDevice() |> DependencyError.ofValidation source
  }

  member _.DeleteDevice (device : RegisteredDevice) =
    deviceDb.DeleteDevice (DbRegisteredDevice.FromDevice device)

  member _.GetUtcNow () = DateTime.UtcNow
