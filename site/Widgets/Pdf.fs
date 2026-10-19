[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.Pdf

type Page = { Number: int; Count: int }

type Model =
  | Opening
  | Showing of Page
  | Broken

type Msg =
  | Opened of pageCount: int
  | Failed
  | Previous
  | Next

type Effect =
  | Open of file: string
  | Draw of pageNumber: int

let init (file: string) : Model * Effect option = Opening, Some(Open file)

let private show (page: Page) = Showing page, Some(Draw page.Number)

let hasPrevious (model: Model) =
  match model with
  | Showing page -> page.Number > 1
  | Opening
  | Broken -> false

let hasNext (model: Model) =
  match model with
  | Showing page -> page.Number < page.Count
  | Opening
  | Broken -> false

let update (msg: Msg) (model: Model) : Model * Effect option =
  match model, msg with
  | Opening, Opened count when count > 0 -> show { Number = 1; Count = count }
  | Opening, Opened _ -> Broken, None
  | (Opening | Showing _), Failed -> Broken, None
  | Showing page, Previous when hasPrevious model -> show { page with Number = page.Number - 1 }
  | Showing page, Next when hasNext model -> show { page with Number = page.Number + 1 }
  | _ -> model, None
