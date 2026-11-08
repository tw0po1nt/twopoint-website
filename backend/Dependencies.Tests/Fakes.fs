module TwoPoint.Dependencies.Tests.Fakes

open TwoPoint.Dependencies.Comms
open TwoPoint.Dependencies.Util

open FsToolkit.ErrorHandling

open System

let now = DateTime(2026, 11, 8, 9, 30, 0, DateTimeKind.Utc)

let ok result = result |> Result.defaultWith (fun error -> failwithf "%A" error)

/// Throws when the test asks for more IDs than `ids` contains.
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
