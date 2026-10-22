[<RequireQualifiedAccess>]
module TwoPoint.Site.Render.Time

open System
open System.Globalization

let isoTime (time: DateTimeOffset) =
  time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)

let shownDate (time: DateTimeOffset) =
  time.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
