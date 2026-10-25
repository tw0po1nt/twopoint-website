[<RequireQualifiedAccess>]
module TwoPoint.Admin.Problem

open System

/// Where the author goes to fix a problem.
type Target =
  /// `InSheet` is true for a control of the Details sheet, and false for a field of the writing column.
  | Control of id: string * inSheet: bool
  /// A line of the body text in the editor, from 1.
  | BodyLine of line: int

let private controlOf (field: string) =
  match field with
  | "title" -> Some(Control("editor-title", false))
  | "excerpt" -> Some(Control("editor-excerpt", false))
  | "author" -> Some(Control("details-author", true))
  | "category" -> Some(Control("details-category", true))
  | "tags" -> Some(Control("details-tags", true))
  | "series" -> Some(Control("details-series", true))
  | "image" -> Some(Control("details-image", true))
  | _ -> None

/// Body line 1 of a problem is the blank line between the front matter and the body text.
let private editorLine (bodyLine: int) = max 1 (bodyLine - 1)

let targetOf (problem: Api.PreviewProblem) : Target option =
  match problem.Place with
  | Some place when place.StartsWith "Field " -> controlOf (place.Substring "Field ".Length)
  | Some place when place.StartsWith "Body line " ->
    match Int32.TryParse(place.Substring "Body line ".Length) with
    | true, line -> Some(BodyLine(editorLine line))
    | _ -> None
  | _ -> None
