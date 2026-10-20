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
  /// The width of the page changed, so the bitmap of the page is not sharp.
  | Resized
  /// The reader clicked a link to a page of the PDF.
  | GoTo of pageNumber: int

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
  | Showing page, Resized -> show page
  | Showing page, GoTo number when number >= 1 && number <= page.Count && number <> page.Number ->
    show { page with Number = number }
  | _ -> model, None
