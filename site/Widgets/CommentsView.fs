[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.CommentsView

open TwoPoint.Api.Comments

open Browser
open Browser.Types
open Fable.Core
open Oxpecker.Solid
open Oxpecker.Solid.Aria
open Thoth.Json

let private perform (props: Contract.CommentsProps) (effect: Comments.Effect) (dispatch: Comments.Msg -> unit) =
  match effect with
  | Comments.Load ->
    Comments.getCommentsForPost props.Api props.Slug
    |> Promise.map (Comments.ListAnswered >> dispatch)
    |> Promise.catch (fun _ -> dispatch Comments.ListUnanswered)
    |> Promise.start
  | Comments.Validate validation ->
    Comments.validateCommenter props.Api validation
    |> Promise.map (Comments.ValidationAnswered >> dispatch)
    |> Promise.catch (fun _ -> dispatch Comments.Unanswered)
    |> Promise.start
  | Comments.Post comment ->
    Comments.postComment props.Api props.Slug comment
    |> Promise.map (Comments.PostAnswered >> dispatch)
    |> Promise.catch (fun _ -> dispatch Comments.Unanswered)
    |> Promise.start

/// A storage call that throws, or a stored value that is not valid, gives no validations.
let private readValidations () =
  try
    match localStorage.getItem Comments.storageKey with
    | null -> Map.empty
    | json -> Decode.fromString (Decode.dict Decode.string) json |> Result.defaultValue Map.empty
  with _ ->
    Map.empty

/// A storage call that throws stores nothing.
let private writeValidations (validations: Map<string, string>) =
  try
    let json = validations |> Map.map (fun _ id -> Encode.string id) |> Encode.dict |> Encode.toString 0
    localStorage.setItem (Comments.storageKey, json)
  with _ ->
    ()

[<Emit "[...new URLSearchParams($0)]">]
let private queryPairs (search: string) : (string * string) array = jsNative

let private takeReturn () =
  let returned = Comments.parse (List.ofArray (queryPairs window.location.search))

  match returned with
  | Some returned ->
    writeValidations ((readValidations ()).Add(returned.Email, returned.ValidationId))
    history.replaceState (null, "", window.location.pathname + window.location.hash)
  | None -> ()

  returned

let private text (event: Event) = (event.target :?> HTMLInputElement).value

let private comments (model: Comments.Model) =
  match model.List with
  | Comments.Loaded comments -> Array.ofList comments
  | Comments.Loading
  | Comments.NotLoaded _ -> [||]

let private listText (model: Comments.Model) =
  match model.List with
  | Comments.Loading -> "Loading the comments."
  | Comments.Loaded comments -> Comments.countText comments.Length
  | Comments.NotLoaded reason -> reason

let private listClass (model: Comments.Model) =
  match model.List with
  | Comments.NotLoaded _ -> "comments-count is-failed"
  | Comments.Loading
  | Comments.Loaded _ -> "comments-count"

let private buttonText (model: Comments.Model) =
  if Comments.isSending model then "[sending]" else "[post comment]"

let private statusClass (model: Comments.Model) =
  match model.Status with
  | Comments.Failed _ -> "comment-status is-failed"
  | Comments.Validated
  | Comments.ValidationSent _
  | Comments.Posted -> "comment-status is-done"
  | Comments.Editing
  | Comments.Validating
  | Comments.Posting -> "comment-status"

let private statusText (model: Comments.Model) =
  match model.Status with
  | Comments.Failed reason -> reason
  | Comments.Validated -> "Your email address is verified. Select [post comment] to post your comment."
  | Comments.ValidationSent email ->
    $"The site sent an email to {email}. Open the link in that email to verify your address, then post your comment."
  | Comments.Posted -> "Your comment is posted. It shows here after the author approves it."
  | Comments.Editing
  | Comments.Validating
  | Comments.Posting -> ""

// JSX drops the space at the end of a text line and refuses a `>` in text, so these strings go in as values.
let private nameLabel = "name>"
let private emailLabel = "email>"
let private commentLabel = "comment>"
let private separator = " · "

let private notePrefix =
  "To stop spam, the site sends an email to verify your address, and the author approves each comment. "
  + "Your name shows with your comment. Your email address stays private. See the "

[<SolidComponent>]
let view (props: Contract.CommentsProps) (returned: Comments.Return option) =
  let pageUrl = window.location.origin + window.location.pathname
  let init = Comments.init pageUrl (readValidations ()) returned
  let model, dispatch = Mvu.run init Comments.update (perform props)

  div (class' = "comments") {
    p (class' = listClass (model ())) { listText (model ()) }

    ol (class' = "comments-list") {
      For(each = comments (model ())) {
        yield
          fun comment _ ->
            li (class' = "comment") {
              p (class' = "comment-meta") {
                time (datetime = comment.CreatedDate) { Comments.shownDate comment.CreatedDate }
                separator
                comment.Commenter
              }

              p (class' = "comment-text") { comment.Content }
            }
      }
    }

    form (
      class' = "comment-form",
      novalidate = true,
      onSubmit =
        fun event ->
          event.preventDefault ()
          dispatch Comments.Submitted
    ) {
      label (class' = "comment-field") {
        span () { nameLabel }

        input (
          type' = "text",
          name = "name",
          autocomplete = "name",
          placeholder = "optional",
          maxlength = 256,
          value = model().Name,
          onInput = fun event -> dispatch (Comments.NameChanged(text event))
        )
      }

      label (class' = "comment-field") {
        span () { emailLabel }

        input (
          type' = "email",
          name = "email",
          autocomplete = "email",
          placeholder = "only to verify you",
          value = model().Email,
          onInput = (fun event -> dispatch (Comments.EmailChanged(text event))),
          onBlur = fun _ -> dispatch Comments.EmailLeft
        )
      }

      Show(when' = Comments.showsEmailError (model ())) {
        p (class' = "comment-error", role = "alert") { "Give a valid email address." }
      }

      label (class' = "comment-field") {
        span () { commentLabel }

        textarea(
          name = "comment",
          rows = 4,
          maxlength = 1000,
          onInput = (fun event -> dispatch (Comments.CommentChanged(text event))),
          onBlur = fun _ -> dispatch Comments.CommentLeft
        )
          .attr ("value", model().Comment)
      }

      Show(when' = Comments.showsCommentError (model ())) {
        p (class' = "comment-error", role = "alert") { "Write a comment." }
      }

      button (type' = "submit", disabled = Comments.isSending (model ())) { buttonText (model ()) }
      p(class' = statusClass (model ())).attr ("aria-live", "polite") { statusText (model ()) }

      p (class' = "comment-note") {
        notePrefix
        a (href = "/privacy/") { "privacy policy" }
        "."
      }
    }
  }

/// One argument, because Fable calls a curried function from a dynamic import as `start(a)(b)`.
let start (args: {| Mount: HTMLElement; Props: Contract.CommentsProps |}) =
  let returned = takeReturn ()
  Mount.replace args.Mount (fun () -> view args.Props returned)
