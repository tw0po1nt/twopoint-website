/// The titles and slugs that the two copies of the post slug rule must agree on: `DraftSlug.ofTitle` in the backend and
/// `Slug.ofTitle` in the admin. Core.Tests and Admin.Tests both compile this file.
module TwoPoint.Tests.SlugTitles

/// `None` is a title that gives no slug, because it has no letter or digit.
let table: (string * string option) list =
  [ "Hello world", Some "hello-world"
    "snake_case_title", Some "snake-case-title"
    "__init__ -- _ setup", Some "init-setup"
    "C# & F#", Some "c-and-f"
    "Matt's F# Notes & Tips", Some "matts-f-notes-and-tips"
    "Don’t panic", Some "dont-panic"
    "Café Crème", Some "cafe-creme"
    "The v⃗ector", Some "the-vector"
    "Wait... what?!", Some "wait-what"
    "  --Spaced out--  ", Some "spaced-out"
    "100% sure!", Some "100-sure"
    "", None
    "   ", None
    "?!…", None
    "___", None
    "日本語", None ]
