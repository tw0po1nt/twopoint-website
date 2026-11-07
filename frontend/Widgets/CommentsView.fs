[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.CommentsView

open TwoPoint.Api.Comments

open Browser
open Browser.Types
open Fable.Core
open Oxpecker.Solid
open Oxpecker.Solid.Aria
open Thoth.Json

/// The CSS animation of `is-marked` has the same time.
let private markTime = 2000

let private scrollTo (section: HTMLElement) (scroll: Comments.Scroll) =
  let comment (id: string) =
    match document.getElementById (Comments.elementId id) with
    | null -> None
    | element -> Some element

  // A model change inside a Solid batch gets to the page only after the batch, so the scroll waits for the next frame.
  window.requestAnimationFrame (fun _ ->
    match scroll with
    | Comments.ToComment id ->
      match comment id with
      | Some element ->
        element.scrollIntoView ()
        element.classList.add "is-marked"
        window.setTimeout ((fun () -> element.classList.remove "is-marked"), markTime) |> ignore
      | None -> section.scrollIntoView ()
    | Comments.ToThread id -> comment id |> Option.defaultValue section |> _.scrollIntoView()
    | Comments.ToReply thread ->
      match document.getElementById (Comments.replyFormId thread) with
      | null -> section.scrollIntoView ()
      | element -> element.scrollIntoView ()
    | Comments.ToSection -> section.scrollIntoView ())
  |> ignore

let private perform
  (props: Contract.CommentsProps)
  (section: HTMLElement)
  (effect: Comments.Effect)
  (dispatch: Comments.Msg -> unit)
  =
  match effect with
  | Comments.Load ->
    Comments.getCommentsForPost props.Api props.Slug
    |> Promise.map (Comments.ListAnswered >> dispatch)
    |> Promise.catch (fun _ -> dispatch Comments.ListUnanswered)
    |> Promise.start
  | Comments.Validate(form, validation) ->
    Comments.validateCommenter props.Api validation
    |> Promise.map (fun response -> dispatch (Comments.ValidationAnswered(form, response)))
    |> Promise.catch (fun _ -> dispatch (Comments.Unanswered form))
    |> Promise.start
  | Comments.Post(form, comment) ->
    Comments.postComment props.Api props.Slug comment
    |> Promise.map (fun answer -> dispatch (Comments.PostAnswered(form, answer)))
    |> Promise.catch (fun _ -> dispatch (Comments.Unanswered form))
    |> Promise.start
  | Comments.PreviewComment(form, request) ->
    Comments.previewComment props.Api request.Source
    |> Promise.map (fun response -> dispatch (Comments.Edited(form, Comments.PreviewAnswered(request, response))))
    |> Promise.catch (fun _ -> dispatch (Comments.Edited(form, Comments.PreviewUnanswered request)))
    |> Promise.start
  | Comments.ScrollTo scroll -> scrollTo section scroll

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

let private listText (model: Comments.Model) =
  match model.List with
  | Comments.Loading -> "Loading comments…"
  | Comments.Loaded comments -> Comments.countText comments.Length
  | Comments.NotLoaded reason -> reason

let private listClass (model: Comments.Model) =
  match model.List with
  | Comments.NotLoaded _ -> "comments-count is-failed"
  | Comments.Loading
  | Comments.Loaded _ -> "comments-count"

let private buttonText (model: Comments.Model) =
  if Comments.isSending model.PageStatus then "[sending]" else "[post comment]"

let private statusClass (status: Comments.Status) =
  match status with
  | Comments.Failed _ -> "comment-status is-failed"
  | Comments.Validated
  | Comments.ValidationSent _
  | Comments.Posted _ -> "comment-status is-done"
  | Comments.Editing
  | Comments.Validating
  | Comments.Posting -> "comment-status"

let private statusText (model: Comments.Model) =
  match model.PageStatus with
  | Comments.Failed reason -> reason
  | Comments.Validated -> "Email verified. Click [post comment] to post."
  | Comments.ValidationSent email ->
    $"Check your inbox. We sent a verification link to {email}. Open it, then post your comment."
  | Comments.Posted Approved -> "Comment posted."
  | Comments.Posted AwaitsApproval -> "Thanks! Your comment will appear here once it's approved."
  | Comments.Editing
  | Comments.Validating
  | Comments.Posting -> ""

// JSX drops the space at the end of a text line and refuses a `>` in text, so these strings go in as values.
let private nameLabel = "name>"
let private emailLabel = "email>"
let private separator = " · "

let private helpText =
  "Markdown works: *italic*, **bold**, `code`, [text](https://…), lists, > quotes, and ``` code blocks. "

let private helpNote =
  "A single newline makes a line break. Headings, images, and tables show as plain text."

let private isPreview (editor: Comments.Editor) =
  match editor.Tab with
  | Comments.Write -> false
  | Comments.Preview _ -> true

type private PreviewPart =
  { Class: string
    Text: string
    Retry: bool
    Html: string option }

/// The write tab shows no preview, so it gets the empty part. `noun` is "comment" or "reply".
let private previewPart (noun: string) (editor: Comments.Editor) =
  let part = { Class = "comment-preview"; Text = ""; Retry = false; Html = None }

  match editor.Tab with
  | Comments.Preview Comments.PreviewState.Empty ->
    { part with Class = "comment-preview is-muted"; Text = $"Nothing to preview. Write a {noun} first." }
  | Comments.Preview Comments.PreviewState.Loading -> { part with Class = "comment-preview is-muted"; Text = "Loading preview…" }
  | Comments.Preview Comments.PreviewState.Failed ->
    { part with
        Class = "comment-preview is-failed"
        Text = $"Couldn't load the preview. You can still post your {noun}. "
        Retry = true }
  | Comments.Preview(Comments.PreviewState.Shown html) -> { part with Html = Some html }
  | Comments.Write -> part

let private notePrefix =
  "To keep out spam, you'll verify your email, and comments may be reviewed before they appear. "
  + "Your name is shown with your comment; your email stays private. See the "

let private authorBadge = " [author]"

let private hasBadge (byline: Comments.Byline) =
  match byline with
  | Comments.PostAuthor _ -> true
  | Comments.Reader _ -> false

[<SolidComponent>]
let private entry (author: string) (comment: TwoPoint.Api.Comments.Comment) =
  let byline = Comments.byline author comment

  Fragment() {
    p (class' = "comment-meta") {
      time (datetime = comment.CreatedDate) { Comments.shownDate comment.CreatedDate }
      separator
      Comments.bylineName byline

      Show(when' = hasBadge byline) {
        span (class' = "comment-badge") { authorBadge }
      }
    }

    // The backend gives only HTML that it made from the Markdown of the comment.
    div (class' = "comment-text", innerHTML = comment.Content)
  }

let private replyClass (comment: TwoPoint.Api.Comments.Comment) =
  match comment.Writer with
  | Author -> "comment is-author"
  | Commenter -> "comment"

/// `noun` is "comment" or "reply", and it is also the name of the text box.
[<SolidComponent>]
let private editorView
  (noun: string)
  (editor: unit -> Comments.Editor)
  (locked: unit -> bool)
  (dispatch: Comments.Edit -> unit)
  =
  let textBox: HTMLElement option ref = ref None
  let previewHeight, setPreviewHeight = createSignal 0.0
  let part () = previewPart noun (editor ())

  let selectPreview () =
    textBox.Value |> Option.iter (fun box -> setPreviewHeight box.offsetHeight)
    dispatch Comments.PreviewSelected

  div (class' = "comment-editor") {
    div (class' = "comment-tabs", role = "tablist") {
      span () { noun + ">" }

      button (
        type' = "button",
        role = "tab",
        class' = "comment-tab",
        ariaSelected = not (isPreview (editor ())),
        onClick = fun _ -> dispatch Comments.WriteSelected
      ) {
        "[write]"
      }

      button (type' = "button", role = "tab", class' = "comment-tab", ariaSelected = isPreview (editor ()), onClick = fun _ -> selectPreview ()) {
        "[preview]"
      }
    }

    textarea(
      class' = (if isPreview (editor ()) then "is-hidden" else ""),
      name = noun,
      rows = 4,
      maxlength = 1000,
      ariaLabel = noun,
      disabled = locked (),
      onInput = (fun event -> dispatch (Comments.TextChanged(text event))),
      onBlur = fun _ -> dispatch Comments.TextLeft
    )
      .ref(fun element -> textBox.Value <- Some element)
      .attr ("value", editor().Text)

    Show(when' = isPreview (editor ())) {
      div(class' = (part ()).Class).attr("style", $"min-height: {previewHeight ()}px").attr ("aria-live", "polite") {
        (part ()).Text

        Show(when' = (part ()).Retry) {
          button (type' = "button", onClick = fun _ -> dispatch Comments.RetrySelected) { "[try again]" }
        }

        Show(when' = (part ()).Html.IsSome) {
          div (class' = "comment-text", innerHTML = ((part ()).Html |> Option.defaultValue ""))
        }
      }
    }

    p (class' = "comment-help") {
      span () { helpText }

      button (type' = "button", ariaExpanded = editor().HelpShown, onClick = fun _ -> dispatch Comments.HelpToggled) {
        "[markdown help]"
      }
    }

    Show(when' = editor().HelpShown) {
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

[<SolidComponent>]
let private identityFields (model: unit -> Comments.Model) (locked: unit -> bool) (dispatch: Comments.Msg -> unit) =
  Fragment() {
    label (class' = "comment-field") {
      span () { nameLabel }

      input (
        type' = "text",
        name = "name",
        autocomplete = "name",
        placeholder = "optional",
        maxlength = 256,
        disabled = locked (),
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
        disabled = locked (),
        value = model().Email,
        onInput = (fun event -> dispatch (Comments.EmailChanged(text event))),
        onBlur = fun _ -> dispatch Comments.EmailLeft
      )
    }

    Show(when' = Comments.showsEmailError (model ())) {
      p (class' = "comment-error", role = "alert") { "Enter a valid email address." }
    }
  }

let private replyOf (model: Comments.Model) =
  model.Reply |> Option.defaultValue { Thread = ""; Editor = Comments.emptyEditor; Identity = Comments.Fields; Status = Comments.Editing }

let private replyButtonText (status: Comments.Status) =
  if Comments.isSending status then "[sending]" else "[post reply]"

[<SolidComponent>]
let private replyFormView (author: string) (thread: TwoPoint.Api.Comments.Comment) (model: unit -> Comments.Model) (dispatch: Comments.Msg -> unit) =
  let target = Comments.Form.Reply thread.Id
  let reply () = replyOf (model ())
  let locked () = Comments.isLocked (reply ()).Status

  form (
    class' = "comment-form comment-reply-form",
    id = Comments.replyFormId thread.Id,
    novalidate = true,
    onSubmit =
      fun event ->
        event.preventDefault ()
        dispatch (Comments.Submitted target)
  ) {
    p (class' = "comment-note") { Comments.replyToText author thread }

    Show(when' = Comments.showsIdentityLine (model ()), fallback = identityFields model locked dispatch) {
      p (class' = "comment-note") {
        $"as {Comments.shownName (model ())} ({model().Email.Trim()}) "
        button (type' = "button", disabled = locked (), onClick = fun _ -> dispatch Comments.IdentityChanged) { "[change]" }
      }
    }

    editorView "reply" (fun () -> (reply ()).Editor) locked (fun change -> dispatch (Comments.Edited(target, change)))

    Show(when' = Comments.showsTextError (reply ()).Editor) {
      p (class' = "comment-error", role = "alert") { "Write a reply." }
    }

    p (class' = "comment-actions") {
      button (type' = "submit", disabled = locked ()) { replyButtonText (reply ()).Status }
      button (type' = "button", disabled = Comments.isSending (reply ()).Status, onClick = fun _ -> dispatch Comments.ReplyCanceled) {
        "[cancel]"
      }
    }

    p(class' = statusClass (reply ()).Status).attr ("aria-live", "polite") { Comments.replyStatusText (reply ()).Status }
  }

[<SolidComponent>]
let view (props: Contract.CommentsProps) (section: HTMLElement) (returned: Comments.Return option) (link: Comments.Link option) =
  let pageUrl = window.location.origin + window.location.pathname
  let init = Comments.init pageUrl (readValidations ()) returned link
  let model, dispatch = Mvu.run init Comments.update (perform props section)
  let list = createMemo (fun () -> model().List)
  let expanded = createMemo (fun () -> model().Expanded)
  let threads = createMemo (fun () -> Array.ofList (Comments.shownThreads (expanded ()) (list ())))
  let pageLocked () = Comments.isLocked (model ()).PageStatus

  div (class' = "comments") {
    p (class' = listClass (model ())) { listText (model ()) }

    ol (class' = "comments-list") {
      For(each = threads ()) {
        yield
          fun thread _ ->
            li (class' = "comment-thread") {
              div (class' = "comment", id = Comments.elementId thread.Comment.Id) { entry props.Author thread.Comment }

              Show(when' = (thread.HiddenEarlier > 0 || not thread.Replies.IsEmpty)) {
                ol (class' = "comment-replies") {
                  Show(when' = (thread.HiddenEarlier > 0)) {
                    li (class' = "comment-earlier") {
                      button (type' = "button", onClick = fun _ -> dispatch (Comments.EarlierShown thread.Comment.Id)) {
                        Comments.earlierText thread.HiddenEarlier
                      }
                    }
                  }

                  For(each = Array.ofList thread.Replies) {
                    yield
                      fun reply _ ->
                        li (class' = replyClass reply, id = Comments.elementId reply.Id) { entry props.Author reply }
                  }
                }
              }

              Show(when' = model().Awaiting.Contains thread.Comment.Id) {
                p (class' = "comment-status is-done") { Comments.awaitingText }
              }

              div (class' = "comment-thread-end") {
                Show(
                  when' = Comments.isReplyOpen thread.Comment.Id (model ()),
                  fallback =
                    button (type' = "button", onClick = fun _ -> dispatch (Comments.ReplyOpened thread.Comment.Id)) {
                      "[reply]"
                    }
                ) {
                  replyFormView props.Author thread.Comment model dispatch
                }
              }
            }
      }
    }

    form (
      class' = "comment-form",
      novalidate = true,
      onSubmit =
        fun event ->
          event.preventDefault ()
          dispatch (Comments.Submitted Comments.Form.Page)
    ) {
      identityFields model pageLocked dispatch

      editorView "comment" (fun () -> model().Page) pageLocked (fun change -> dispatch (Comments.Edited(Comments.Form.Page, change)))

      Show(when' = Comments.showsTextError (model ()).Page) {
        p (class' = "comment-error", role = "alert") { "Write a comment." }
      }

      button (type' = "submit", disabled = pageLocked ()) { buttonText (model ()) }
      p(class' = statusClass (model ()).PageStatus).attr ("aria-live", "polite") { statusText (model ()) }

      p (class' = "comment-note") {
        notePrefix
        a (href = "/privacy/") { "privacy policy" }
        "."
      }
    }
  }

/// One argument, because Fable calls a curried function from a dynamic import as `start(a)(b)`.
let start (args: {| Mount: HTMLElement; Props: Contract.CommentsProps |}) =
  let link = Comments.parseLink window.location.hash (List.ofArray (queryPairs window.location.search))
  let returned = takeReturn ()
  let section =
    args.Mount.closest "details"
    |> Option.map (fun panel -> panel :?> HTMLElement)
    |> Option.defaultValue args.Mount
  Mount.replace args.Mount (fun () -> view args.Props section returned link)
