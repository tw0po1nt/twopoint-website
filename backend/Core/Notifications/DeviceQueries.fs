namespace TwoPoint.Core.Notifications.Queries

open TwoPoint.Core.Notifications
open TwoPoint.Core.Posts
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

[<RequireQualifiedAccess>]
module ListPushDevices =

  type Query = string -> CancellableTask<QueryResult<RegisteredDevice list>>

  type Dependencies =
    abstract member GetDevicesOfUser: UserExternalId -> DependencyResult<RegisteredDevice list>

  let withDependencies (dependencies : Dependencies) : Query =
    fun userId -> cancellableTaskResult {
      let! userId = userId |> UserExternalId.create |> Result.mapError QueryError.Validation
      let! devices = dependencies.GetDevicesOfUser userId |> CancellableTaskResult.mapError QueryError.Dependency
      return devices |> List.sortByDescending (fun (device : RegisteredDevice) -> device.CreatedDate)
    }
