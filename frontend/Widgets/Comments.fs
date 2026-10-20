[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.Comments

open TwoPoint.Api
open TwoPoint.Api.Comments

open System

/// The privacy policy names this `localStorage` key.
let storageKey = "commenterValidations"

/// The backend sets this query key.
[<Literal>]
let validationIdKey = "commenterValidationId"

type Return =
  { Email: string
    Name: string
    Comment: string
    ValidationId: string }

type CommentList =
  | Loading
  | Loaded of Comment list
  | NotLoaded of reason: string

type Status =
  | Editing
  | Validated
  | Validating
  | ValidationSent of email: string
  | Posting
  | Posted
  | Failed of reason: string

type Model =
  { /// The URL of the post page, with no query and no fragment.
    PageUrl: string
    /// The validation ID of each email address.
    Validations: Map<string, string>
    List: CommentList
    Email: string
    Name: string
    Comment: string
    EmailTouched: bool
    CommentTouched: bool
    Status: Status }

type Msg =
  | ListAnswered of ApiResponse<Comment list>
  | ListUnanswered
  | EmailChanged of string
  | NameChanged of string
  | CommentChanged of string
  | EmailLeft
  | CommentLeft
  | Submitted
  | ValidationAnswered of ApiResponse<unit>
  | PostAnswered of ApiResponse<unit>
  | Unanswered

type Effect =
  | Load
  | Validate of CommenterValidation
  | Post of NewComment

let parse (query: (string * string) list) : Return option =
  let find key =
    query
    |> List.tryFind (fun (name, value) -> name = key && value.Trim() <> "")
    |> Option.map snd

  match find "email", find "comment", find validationIdKey with
  | Some email, Some comment, Some validationId ->
    Some
      { Email = email.Trim()
        Name = find "name" |> Option.defaultValue ""
        Comment = comment
        ValidationId = validationId.Trim() }
  | _ -> None

let private empty (pageUrl: string) (validations: Map<string, string>) =
  { PageUrl = pageUrl
    Validations = validations
    List = Loading
    Email = ""
    Name = ""
    Comment = ""
    EmailTouched = false
    CommentTouched = false
    Status = Editing }

let init (pageUrl: string) (validations: Map<string, string>) (returned: Return option) : Model * Effect option =
  let model =
    match returned with
    | Some returned ->
      { empty pageUrl (validations.Add(returned.Email, returned.ValidationId)) with
          Email = returned.Email
          Name = returned.Name
          Comment = returned.Comment
          Status = Validated }
    | None -> empty pageUrl validations

  model, Some Load

let isValidComment (comment: string) = comment.Trim() <> ""

let showsEmailError (model: Model) =
  model.EmailTouched && not (Subscribe.isValidEmail model.Email)

let showsCommentError (model: Model) =
  model.CommentTouched && not (isValidComment model.Comment)

let isSending (model: Model) =
  match model.Status with
  | Validating
  | Posting -> true
  | Editing
  | Validated
  | ValidationSent _
  | Posted
  | Failed _ -> false

/// After a sent validation, the form stays locked until the reader comes back through the link in the email.
let isLocked (model: Model) =
  match model.Status with
  | Validating
  | Posting
  | ValidationSent _ -> true
  | Editing
  | Validated
  | Posted
  | Failed _ -> false

/// The backend adds the validation ID to the query of this URL.
let redirectUri (model: Model) =
  let name = model.Name.Trim()

  let query =
    [ "email", model.Email.Trim()
      if name <> "" then
        "name", name
      "comment", model.Comment.Trim() ]
    |> List.map (fun (key, value) -> $"{key}={Uri.EscapeDataString value}")
    |> String.concat "&"

  $"{model.PageUrl}?{query}#comments"

let shownDate (createdDate: string) =
  if createdDate.Length >= 10 then createdDate.Substring(0, 10) else createdDate

let countText (count: int) =
  match count with
  | 0 -> "0 comments. Be the first."
  | 1 -> "1 comment"
  | count -> $"{count} comments"

let private listFailed = "The comments did not load. Try again later."

let private refused = "The comment did not go through. Try again later."

let private unanswered = "The site cannot get to the server. Try again later."

let private failed (response: ApiResponse<unit>) =
  Failed(response.Message |> Option.defaultValue refused)

let private submit (model: Model) =
  let email = model.Email.Trim()
  let comment = model.Comment.Trim()

  match model.Validations.TryFind email with
  | Some validationId ->
    { model with Status = Posting }, Some(Post { ValidationId = validationId; Comment = comment })
  | None ->
    let name = model.Name.Trim()

    { model with Status = Validating },
    Some(
      Validate
        { EmailAddress = email
          Name = if name = "" then None else Some name
          RedirectUri = redirectUri model }
    )

let update (msg: Msg) (model: Model) : Model * Effect option =
  match msg with
  | ListAnswered response when response.Success -> { model with List = Loaded(response.Data |> Option.defaultValue []) }, None
  | ListAnswered response -> { model with List = NotLoaded(response.Message |> Option.defaultValue listFailed) }, None
  | ListUnanswered -> { model with List = NotLoaded unanswered }, None
  | EmailChanged email -> { model with Email = email }, None
  | NameChanged name -> { model with Name = name }, None
  | CommentChanged comment -> { model with Comment = comment }, None
  | EmailLeft -> { model with EmailTouched = model.EmailTouched || model.Email.Trim() <> "" }, None
  | CommentLeft -> { model with CommentTouched = model.CommentTouched || model.Comment <> "" }, None
  | Submitted when isLocked model -> model, None
  | Submitted when not (Subscribe.isValidEmail model.Email && isValidComment model.Comment) ->
    { model with
        EmailTouched = true
        CommentTouched = true },
    None
  | Submitted -> submit model
  | ValidationAnswered response when response.Success -> { model with Status = ValidationSent(model.Email.Trim()) }, None
  | ValidationAnswered response -> { model with Status = failed response }, None
  | PostAnswered response when response.Success ->
    { empty model.PageUrl model.Validations with
        List = model.List
        Status = Posted },
    None
  | PostAnswered response -> { model with Status = failed response }, None
  | Unanswered -> { model with Status = Failed unanswered }, None
