module TwoPoint.Site.Dev.Tests.WatchTests

open TwoPoint.DevHost

open Expecto
open System
open System.IO

[<Tests>]
let tests =
  testList "Watch" [
    test "an npm watch that cannot start gives an error and does not throw" {
      let folder = Path.Combine(Path.GetTempPath(), $"no-widgets-{Guid.NewGuid():N}")
      Expect.isError (Watch.npmWatch folder) "The watch does not start"
    }
  ]
