module TwoPoint.Dependencies.Tests.Fakes

open TwoPoint.Dependencies.Comms
open TwoPoint.Dependencies.Util

open FsToolkit.ErrorHandling

open System

/// Gives the IDs in the order of `ids`, and gives `now` at each read of the clock.
type FixedSystem(ids : Guid list, now : DateTime) =
  let mutable rest = ids

  interface ISystemDependencies with
    member _.NewGuid () =
      match rest with
      | id :: others ->
        rest <- others
        id
      | [] -> failwith "The test gives no more IDs"

    member _.GetUtcNow () = now

let notUsed () = failwith "The test does not use this member"

type internal RecordingCommsDb() =
  let emails = ResizeArray<DbEmail>()
  let pushes = ResizeArray<DbPush>()

  member _.Emails = List.ofSeq emails
  member _.Pushes = List.ofSeq pushes

  interface ICommsDb with
    member _.GetEmailByOperationId _ = notUsed ()

    member _.CreateEmail email =
      emails.Add email
      CancellableTaskResult.singleton email

    member _.UpdateEmailStatus _ _ _ = notUsed ()

    member _.CreatePush push =
      pushes.Add push
      CancellableTaskResult.singleton push
