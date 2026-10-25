/// The images and the PDFs that the author pastes, drops, or picks while the editor is open.
[<RequireQualifiedAccess>]
module TwoPoint.Admin.Upload

type Kind =
  | Image
  | Pdf

type State =
  | Uploading
  /// The API gave the URL. The editor writes the Markdown for it and then sends `Inserted`.
  | Ready of url: string
  /// The API refused the file or did not answer. The author can retry.
  | Failed of reason: string
  /// The admin did not send the file. A retry gives the same answer, so the author can only dismiss it.
  | Refused of reason: string

type Item =
  { Id: int
    Name: string
    /// None when the type of the file is not allowed.
    Kind: Kind option
    /// The browser file. The model keeps it for a retry.
    Source: obj
    /// The offset in the body where the placeholder shows.
    At: int
    State: State }

type Model = { Next: int; Items: Item list }

type Dropped =
  { Name: string
    ContentType: string
    Size: float
    Source: obj
    At: int }

type Msg =
  | Dropped of Dropped
  | Answered of id: int * Result<string, Api.Failure>
  | Retried of id: int
  | Dismissed of id: int
  | Inserted of id: int

type Effect = Send of id: int * source: obj

let init = { Next = 1; Items = [] }

let maxBytes = 25. * 1024. * 1024.

let private extensionKinds =
  [ ".jpg", Image
    ".jpeg", Image
    ".png", Image
    ".webp", Image
    ".gif", Image
    ".pdf", Pdf ]

/// The type of the file from its content type, and from its name when the browser gives no content type.
let kindOf (contentType: string) (name: string) =
  match contentType with
  | "image/jpeg"
  | "image/png"
  | "image/webp"
  | "image/gif" -> Some Image
  | "application/pdf" -> Some Pdf
  | "" ->
    extensionKinds
    |> List.tryFind (fun (extension, _) -> name.ToLowerInvariant().EndsWith extension)
    |> Option.map snd
  | _ -> None

let private find (id: int) (model: Model) =
  model.Items |> List.tryFind (fun item -> item.Id = id)

let private change (id: int) (state: State) (model: Model) =
  { model with
      Items = model.Items |> List.map (fun item -> if item.Id = id then { item with State = state } else item) }

let private remove (id: int) (model: Model) =
  { model with Items = model.Items |> List.filter (fun item -> item.Id <> id) }

let update (msg: Msg) (model: Model) : Model * Effect option =
  match msg with
  | Dropped file ->
    let kind = kindOf file.ContentType file.Name

    let state, effect =
      match kind with
      | None -> Refused $"{file.Name} is not a JPEG, PNG, WebP, GIF, or PDF file.", None
      | Some _ when file.Size > maxBytes -> Refused $"{file.Name} is larger than 25 MB.", None
      | Some _ -> Uploading, Some(Send(model.Next, file.Source))

    { Next = model.Next + 1
      Items =
        model.Items
        @ [ { Id = model.Next
              Name = file.Name
              Kind = kind
              Source = file.Source
              At = file.At
              State = state } ] },
    effect
  | Answered(id, result) ->
    match find id model, result with
    | Some { State = Uploading }, Ok url -> change id (Ready url) model, None
    | Some { State = Uploading }, Error failure -> change id (Failed(Api.reason failure)) model, None
    | _ -> model, None
  | Retried id ->
    match find id model with
    | Some { State = Failed _ } as item -> change id Uploading model, item |> Option.map (fun item -> Send(id, item.Source))
    | _ -> model, None
  | Dismissed id ->
    match find id model with
    | Some { State = Failed _ | Refused _ } -> remove id model, None
    | _ -> model, None
  | Inserted id ->
    match find id model with
    | Some { State = Ready _ } -> remove id model, None
    | _ -> model, None

/// An upload that waits for the author to retry or dismiss it.
let isFailed (item: Item) =
  match item.State with
  | Failed _
  | Refused _ -> true
  | Uploading
  | Ready _ -> false

let failureReason (item: Item) =
  match item.State with
  | Failed reason
  | Refused reason -> reason
  | Uploading
  | Ready _ -> ""

/// The Markdown that replaces the placeholder, and the offset of the cursor in it.
/// An image has the cursor between the brackets of the alt text. A PDF line is alone between blank lines.
/// `before` and `after` are the body text before and after the placeholder.
let insertion (kind: Kind) (url: string) (before: string) (after: string) : string * int =
  match kind with
  | Image -> $"![]({url})", 2
  | Pdf ->
    let lead =
      if before = "" || before.EndsWith "\n\n" then ""
      elif before.EndsWith "\n" then "\n"
      else "\n\n"

    let trail =
      if after = "" || after.StartsWith "\n\n" then ""
      elif after.StartsWith "\n" then "\n"
      else "\n\n"

    let line = $"::pdf{{file=\"{url}\"}}"
    let text = lead + line + trail
    text, (lead + line).Length
