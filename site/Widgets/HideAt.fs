[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.HideAt

// Each function here must compile on .NET, and with Fable to plain JavaScript with no `fable-library` code.

/// The attribute of an element that the head script hides after a publish date, for example the upcoming post banner.
let attribute = "data-hide-at"

/// `publishDate` and `now` are ISO 8601 UTC times in the form of `Date.toISOString`, for example
/// `2026-10-29T13:00:00.000Z`. Two texts of this form compare in time order. A publish date of another form is never past.
let isPast (now: string) (publishDate: string) =
  publishDate.Length = 24 && publishDate.EndsWith "Z" && publishDate <= now
