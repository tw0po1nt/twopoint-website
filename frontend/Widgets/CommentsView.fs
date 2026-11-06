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
  | Comments.PreviewComment request ->
    Comments.previewComment props.Api request.Source
    |> Promise.map (fun response -> dispatch (Comments.PreviewAnswered(request, response)))
    |> Promise.catch (fun _ -> dispatch (Comments.PreviewUnanswered request))
    |> Promise.start

let private readValidations () =
  try
    match localStorage.getItem Comments.storageKey with
    | null -> Map.empty
    | json -> Decode.fromString (Decode.dict Decode.string) json |> Result.defaultValue Map.empty
  with _ ->
    Map.empty

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
  | Comments.Posted _ -> "comment-status is-done"
  | Comments.Editing
  | Comments.Validating
  | Comments.Posting -> "comment-status"

let private statusText (model: Comments.Model) =
  match model.Status with
  | Comments.Failed reason -> reason
  | Comments.Validated -> "Your email address is verified. Select [post comment] to post your comment."
  | Comments.ValidationSent email ->
    $"The site sent an email to {email}. Open the link in that email to verify your address, then post your comment."
  | Comments.Posted Approved -> "Your comment is posted."
  | Comments.Posted AwaitsApproval -> "Your comment is posted. It shows here after the author approves it."
  | Comments.Editing
  | Comments.Validating
  | Comments.Posting -> ""

// JSX drops the space at the end of a text line and refuses a `>` in text, so these strings go in as values.
let private nameLabel = "name>"
let private emailLabel = "email>"
let private commentLabel = "comment>"
let private separator = " · "

let private helpText =
  "Markdown works: *italic*, **bold**, `code`, [text](https://…), lists, > quotes, and ``` code blocks. "

let private helpNote =
  "One new line is a line break. Headings, images, and tables stay as you type them."

let private isPreview (model: Comments.Model) =
  match model.Tab with
  | Comments.Write -> false
  | Comments.Preview _ -> true

type private PreviewPart =
  { Class: string
    Text: string
    Retry: bool
    Html: string option }

/// The write tab shows no preview, so it gets the empty part.
let private previewPart (model: Comments.Model) =
  let part = { Class = "comment-preview"; Text = ""; Retry = false; Html = None }

  match model.Tab with
  | Comments.Preview Comments.PreviewState.Empty ->
    { part with Class = "comment-preview is-muted"; Text = "Nothing to preview. Write a comment first." }
  | Comments.Preview Comments.PreviewState.Loading -> { part with Class = "comment-preview is-muted"; Text = "Loading the preview…" }
  | Comments.Preview Comments.PreviewState.Failed ->
    { part with
        Class = "comment-preview is-failed"
        Text = "The site cannot show the preview now. You can still post your comment. "
        Retry = true }
  | Comments.Preview(Comments.PreviewState.Shown html) -> { part with Html = Some html }
  | Comments.Write -> part

let private notePrefix =
  "To stop spam, the site sends an email to verify your address, and the author approves each comment. "
  + "Your name shows with your comment. Your email address stays private. See the "

[<SolidComponent>]
let view (props: Contract.CommentsProps) (returned: Comments.Return option) =
  let pageUrl = window.location.origin + window.location.pathname
  let init = Comments.init pageUrl (readValidations ()) returned
  let model, dispatch = Mvu.run init Comments.update (perform props)
  let commentBox: HTMLElement option ref = ref None
  let previewHeight, setPreviewHeight = createSignal 0.0

  let selectPreview () =
    commentBox.Value |> Option.iter (fun box -> setPreviewHeight box.offsetHeight)
    dispatch Comments.PreviewSelected

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

              // The backend gives only HTML that it made from the Markdown of the comment.
              div (class' = "comment-text", innerHTML = comment.Content)
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
          disabled = Comments.isLocked (model ()),
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
          disabled = Comments.isLocked (model ()),
          value = model().Email,
          onInput = (fun event -> dispatch (Comments.EmailChanged(text event))),
          onBlur = fun _ -> dispatch Comments.EmailLeft
        )
      }

      Show(when' = Comments.showsEmailError (model ())) {
        p (class' = "comment-error", role = "alert") { "Give a valid email address." }
      }

      div (class' = "comment-editor") {
        div (class' = "comment-tabs", role = "tablist") {
          span () { commentLabel }

          button (
            type' = "button",
            role = "tab",
            class' = "comment-tab",
            ariaSelected = not (isPreview (model ())),
            onClick = fun _ -> dispatch Comments.WriteSelected
          ) {
            "[write]"
          }

          button (type' = "button", role = "tab", class' = "comment-tab", ariaSelected = isPreview (model ()), onClick = fun _ -> selectPreview ()) {
            "[preview]"
          }
        }

        textarea(
          class' = (if isPreview (model ()) then "is-hidden" else ""),
          name = "comment",
          rows = 4,
          maxlength = 1000,
          ariaLabel = "comment",
          disabled = Comments.isLocked (model ()),
          onInput = (fun event -> dispatch (Comments.CommentChanged(text event))),
          onBlur = fun _ -> dispatch Comments.CommentLeft
        )
          .ref(fun element -> commentBox.Value <- Some element)
          .attr ("value", model().Comment)

        Show(when' = isPreview (model ())) {
          div(class' = (previewPart (model ())).Class).attr("style", $"min-height: {previewHeight ()}px").attr ("aria-live", "polite") {
            (previewPart (model ())).Text

            Show(when' = (previewPart (model ())).Retry) {
              button (type' = "button", onClick = fun _ -> dispatch Comments.RetrySelected) { "[try again]" }
            }

            Show(when' = (previewPart (model ())).Html.IsSome) {
              div (class' = "comment-text", innerHTML = ((previewPart (model ())).Html |> Option.defaultValue ""))
            }
          }
        }

        p (class' = "comment-help") {
          span () { helpText }

          button (type' = "button", ariaExpanded = model().HelpShown, onClick = fun _ -> dispatch Comments.HelpToggled) {
            "[markdown help]"
          }
        }

        Show(when' = model().HelpShown) {
          div (class' = "comment-help-sheet") {
            table () {
              thead () {
                tr () {
                  th (scope = "col") { "you type" }
                  th (scope = "col") { "readers see" }
                }
              }

              tbody () {
                For(each = Array.ofList Comments.helpRows) {
                  yield
                    fun row _ ->
                      tr () {
                        td () { pre () { row.YouType } }
                        td (class' = "comment-text", innerHTML = row.ReadersSee)
                      }
                }
              }
            }

            p () { helpNote }
          }
        }
      }

      Show(when' = Comments.showsCommentError (model ())) {
        p (class' = "comment-error", role = "alert") { "Write a comment." }
      }

      button (type' = "submit", disabled = Comments.isLocked (model ())) { buttonText (model ()) }
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
