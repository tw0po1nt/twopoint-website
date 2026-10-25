namespace TwoPoint.Admin

[<RequireQualifiedAccess>]
type Progress<'action> =
  | Ready
  | Sending of 'action
  | NotDone of 'action * reason: string

[<RequireQualifiedAccess>]
module Progress =

  let isSending (progress: Progress<'action>) =
    match progress with
    | Progress.Sending _ -> true
    | Progress.Ready
    | Progress.NotDone _ -> false
