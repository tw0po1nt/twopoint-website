namespace TwoPoint.Core.Notifications

open TwoPoint.Core.Util

open IcedTasks

type PushOutcome =
  | Delivered
  /// The push service answered `404` or `410`.
  | DeviceGone

type Pushed =
  { Sent : RegisteredDevice list
    Removed : RegisteredDevice list
    Failed : (RegisteredDevice * DependencyError) list }

[<RequireQualifiedAccess>]
module DevicePush =

  type private Step =
    | StepSent of RegisteredDevice
    | StepRemoved of RegisteredDevice
    | StepFailed of RegisteredDevice * DependencyError

  let pushAll
    (send : RegisteredDevice -> DependencyResult<PushOutcome>)
    (record : RegisteredDevice -> DependencyResult<unit>)
    (delete : RegisteredDevice -> DependencyResult<unit>)
    (devices : RegisteredDevice list)
    : CancellableTask<Pushed> =

    let pushTo (device : RegisteredDevice) : CancellableTask<Step> = cancellableTask {
      match! send device with
      | Ok Delivered ->
        let! recorded = record device
        return recorded |> Result.map (fun () -> StepSent device) |> Result.defaultWith (fun error -> StepFailed(device, error))
      | Ok DeviceGone ->
        let! deleted = delete device
        return deleted |> Result.map (fun () -> StepRemoved device) |> Result.defaultWith (fun error -> StepFailed(device, error))
      | Error error -> return StepFailed(device, error)
    }

    cancellableTask {
      let steps = ResizeArray<Step>()

      for device in devices do
        let! step = pushTo device
        steps.Add step

      return
        { Sent = steps |> Seq.choose (function StepSent device -> Some device | _ -> None) |> List.ofSeq
          Removed = steps |> Seq.choose (function StepRemoved device -> Some device | _ -> None) |> List.ofSeq
          Failed = steps |> Seq.choose (function StepFailed(device, error) -> Some(device, error) | _ -> None) |> List.ofSeq }
    }
