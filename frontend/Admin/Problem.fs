[<RequireQualifiedAccess>]
module TwoPoint.Admin.Problem

type Surface =
  | Sheet
  | WritingColumn

type Target =
  | Control of id: string * Surface
  | BodyLine of line: int

let private controlOf (field: string) =
  match field with
  | "title" -> Some(Control("editor-title", WritingColumn))
  | "excerpt" -> Some(Control("editor-excerpt", WritingColumn))
  | "author" -> Some(Control("details-author", Sheet))
  | "category" -> Some(Control("details-category", Sheet))
  | "tags" -> Some(Control("details-tags", Sheet))
  | "series" -> Some(Control("details-series", Sheet))
  | "image" -> Some(Control("details-image", Sheet))
  | "showOnHomepage" -> Some(Control("details-show-on-homepage", Sheet))
  | _ -> None

let targetOf (problem: Api.Problem) : Target option =
  match problem.Place with
  | Some(Api.Field name) -> controlOf name
  | Some(Api.BodyLine line) -> Some(BodyLine line)
  | None -> None
