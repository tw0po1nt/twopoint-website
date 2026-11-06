[<RequireQualifiedAccess>]
module TwoPoint.Admin.Upload

open Browser.Types

type Kind =
  | Image
  | Pdf

type State =
  | Uploading
  /// The editor writes the Markdown for the URL and then sends `Inserted`.
  | Ready of url: string
  | Failed of reason: string

type Item =
  { Id: int
    Name: string
    Kind: Kind
    File: File
    At: int
    State: State }

/// A file that the admin did not send.
type Refusal = { Id: int; Reason: string }

type Model =
  { Next: int
    Items: Item list
    Refusals: Refusal list }

type Dropped =
  { Name: string
    ContentType: string
    Size: float
    File: File
    At: int }

type Msg =
  | Dropped of Dropped
  | Answered of id: int * Result<string, Api.Failure>
  | Retried of id: int
  /// The editor read the place of the placeholder before it removed the placeholder of a failed upload.
  | Placed of id: int * at: int
  | Dismissed of id: int
  | Inserted of id: int

type Effect = Send of id: int * file: File

let init = { Next = 1; Items = []; Refusals = [] }

let maxBytes = 25. * 1024. * 1024.

let private extensionKinds =
  [ ".jpg", Image
    ".jpeg", Image
    ".png", Image
    ".webp", Image
    ".gif", Image
    ".pdf", Pdf ]

/// The browser can give an empty content type, for example for a dropped PDF.
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

let private change (id: int) (update: Item -> Item) (model: Model) =
  { model with
      Items = model.Items |> List.map (fun item -> if item.Id = id then update item else item) }

let private remove (id: int) (model: Model) =
  { model with Items = model.Items |> List.filter (fun item -> item.Id <> id) }

let update (msg: Msg) (model: Model) : Model * Effect option =
  match msg with
  | Dropped file ->
    let id = model.Next

    let refuse reason =
      { model with
          Next = id + 1
          Refusals = model.Refusals @ [ { Id = id; Reason = reason } ] },
      None

    match kindOf file.ContentType file.Name with
    | None -> refuse $"{file.Name} isn't a supported type. Use JPEG, PNG, WebP, GIF, or PDF."
    | Some _ when file.Size > maxBytes -> refuse $"{file.Name} is over the 25 MB limit."
    | Some kind ->
      { model with
          Next = id + 1
          Items =
            model.Items
            @ [ { Id = id
                  Name = file.Name
                  Kind = kind
                  File = file.File
                  At = file.At
                  State = Uploading } ] },
      Some(Send(id, file.File))
  | Answered(id, result) ->
    match find id model, result with
    | Some { State = Uploading }, Ok url -> change id (fun item -> { item with State = Ready url }) model, None
    | Some { State = Uploading }, Error failure ->
      change id (fun item -> { item with State = Failed(Api.reason failure) }) model, None
    | _ -> model, None
  | Placed(id, at) ->
    match find id model with
    | Some { State = Failed _ } -> change id (fun item -> { item with At = at }) model, None
    | _ -> model, None
  | Retried id ->
    match find id model with
    | Some({ State = Failed _ } as item) -> change id (fun item -> { item with State = Uploading }) model, Some(Send(id, item.File))
    | _ -> model, None
  | Dismissed id ->
    match find id model with
    | Some { State = Failed _ } -> remove id model, None
    | Some _ -> model, None
    | None -> { model with Refusals = model.Refusals |> List.filter (fun refusal -> refusal.Id <> id) }, None
  | Inserted id ->
    match find id model with
    | Some { State = Ready _ } -> remove id model, None
    | _ -> model, None

type Notice =
  { Id: int
    Title: string
    Reason: string
    CanRetry: bool }

/// Each upload that waits for the author to dismiss it or, for a failed upload, to retry it.
let notices (model: Model) : Notice list =
  [ for item in model.Items do
      match item.State with
      | Failed reason ->
        { Id = item.Id
          Title = $"Couldn't upload {item.Name}."
          Reason = reason
          CanRetry = true }
      | Uploading
      | Ready _ -> ()
    for refusal in model.Refusals do
      { Id = refusal.Id
        Title = "Couldn't upload a file."
        Reason = refusal.Reason
        CanRetry = false } ]

/// A PDF line is alone between blank lines. `before` and `after` are the body text around the placeholder.
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
