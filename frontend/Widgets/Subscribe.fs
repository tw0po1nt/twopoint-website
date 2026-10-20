[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.Subscribe

open TwoPoint.Api
open TwoPoint.Api.Subscriptions

open System.Text.RegularExpressions

type Status =
  | Editing
  | Sending
  | Failed of reason: string
  | Subscribed

type Model =
  { Email: string
    Name: string
    EmailTouched: bool
    Status: Status }

type Msg =
  | EmailChanged of string
  | NameChanged of string
  | EmailLeft
  | Submitted
  | Answered of ApiResponse<unit>
  | Unanswered

type Effect = Send of NewSubscription

let init =
  { Email = ""
    Name = ""
    EmailTouched = false
    Status = Editing }

let private emailPattern = Regex "^\w+([-+.']\w+)*@(\[*\w+)([-.]\w+)*\.\w+([-.]\w+\])*$"

let isValidEmail (email: string) = emailPattern.IsMatch(email.Trim())

let showsEmailError (model: Model) =
  model.EmailTouched && not (isValidEmail model.Email)

let isSending (model: Model) =
  match model.Status with
  | Sending -> true
  | Editing
  | Failed _
  | Subscribed -> false

let private refused = "The subscription did not go through. Try again later."

let private unanswered = "The site cannot get to the server. Try again later."

let update (msg: Msg) (model: Model) : Model * Effect option =
  match msg with
  | EmailChanged email -> { model with Email = email }, None
  | NameChanged name -> { model with Name = name }, None
  | EmailLeft -> { model with EmailTouched = model.EmailTouched || model.Email.Trim() <> "" }, None
  | Submitted when isSending model -> model, None
  | Submitted when not (isValidEmail model.Email) -> { model with EmailTouched = true }, None
  | Submitted ->
    let name = model.Name.Trim()

    { model with Status = Sending },
    Some(
      Send
        { EmailAddress = model.Email.Trim()
          Name = if name = "" then None else Some name }
    )
  | Answered response when response.Success -> { init with Status = Subscribed }, None
  | Answered response -> { model with Status = Failed(response.Message |> Option.defaultValue refused) }, None
  | Unanswered -> { model with Status = Failed unanswered }, None
