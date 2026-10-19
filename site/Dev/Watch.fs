[<RequireQualifiedAccess>]
module TwoPoint.Site.Dev.Watch

open System
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Tasks

/// Calls `onChange` when a burst of changes in the folders stops for `delay`. Two calls never overlap.
let folders (delay: TimeSpan) (folders: string list) (onChange: unit -> unit) : IDisposable =
  let gate = obj ()
  let timer = new Timer((fun _ -> lock gate onChange), null, Timeout.Infinite, Timeout.Infinite)
  let changed _ = timer.Change(delay, Timeout.InfiniteTimeSpan) |> ignore

  let watchers =
    [ for folder in folders do
        Directory.CreateDirectory folder |> ignore
        let watcher = new FileSystemWatcher(folder, IncludeSubdirectories = true)
        watcher.Changed.Add changed
        watcher.Created.Add changed
        watcher.Deleted.Add changed
        watcher.Renamed.Add changed
        watcher.EnableRaisingEvents <- true
        watcher ]

  { new IDisposable with
      member _.Dispose() =
        for watcher in watchers do
          watcher.Dispose()

        timer.Dispose() }

/// Each browser that waits on `Next` gets one reload for each `Send`.
type Reloads() =
  let gate = obj ()
  let mutable next = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)

  member _.Next = lock gate (fun () -> next.Task)

  member _.Send() =
    let previous =
      lock gate (fun () ->
        let previous = next
        next <- TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        previous)

    previous.TrySetResult() |> ignore

/// The process tree stops when the dev process stops.
let widgets (widgetsFolder: string) : Process =
  let watch =
    Process.Start(ProcessStartInfo("npm", "run watch", WorkingDirectory = widgetsFolder, UseShellExecute = false))

  AppDomain.CurrentDomain.ProcessExit.Add(fun _ ->
    if not watch.HasExited then
      watch.Kill true)

  watch
