[<RequireQualifiedAccess>]
module TwoPoint.Site.Render.Time

open System
open System.Globalization

let isoTime (time: DateTimeOffset) =
  time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)

/// The form of `Date.toISOString`, so that the head script can compare the time with the current time as text.
let scriptTime (time: DateTimeOffset) =
  time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)

let shownDate (time: DateTimeOffset) =
  time.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
