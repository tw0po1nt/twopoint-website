[<RequireQualifiedAccess>]
module TwoPoint.Admin.Dates

/// The API sends each date in ISO 8601.
let dateText (date: string) = date.Split 'T' |> Array.head
