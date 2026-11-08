namespace TwoPoint.Dependencies.Util

open System

type ISystemDependencies =
  abstract member NewGuid: unit -> Guid
  abstract member GetUtcNow: unit -> DateTime

[<RequireQualifiedAccess>]
module SystemDependencies =

  let live =
    { new ISystemDependencies with
        member _.NewGuid () = Guid.NewGuid()
        member _.GetUtcNow () = DateTime.UtcNow }
