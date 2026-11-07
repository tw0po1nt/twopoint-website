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

/// The widget sets this query key in the redirect URI of a reply.
[<Literal>]
let replyToKey = "replyTo"

/// `ReplyTo` is the ID of the top-level comment of a reply.
type Return =
  { Email: string
    Name: string
    Comment: string
    ValidationId: string
    ReplyTo: string option }

/// The `thread` query of a reply email names the top-level comment, for a reply that is gone.
type Link = { Comment: string; Thread: string option }

/// The scroll target after the comments load.
type Arrival =
  | Linked of Link
  | Returned of thread: string

type Scroll =
  | ToComment of id: string
  | ToThread of id: string
  | ToReply of thread: string
  | ToSection

type Thread = { Comment: Comment; Replies: Comment list }

type ShownThread =
  { Comment: Comment
    HiddenEarlier: int
    Replies: Comment list }

type Byline =
  | Reader of name: string
  | PostAuthor of name: string

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
  | Posted of Approval
  | Failed of reason: string

[<RequireQualifiedAccess>]
type PreviewState =
  | Empty
  | Loading
  | Failed
  | Shown of html: string

type Tab =
  | Write
  | Preview of PreviewState

type Kept = { Source: string; Html: string }

type Editor =
  { Text: string
    Touched: bool
    Tab: Tab
    /// The last source that the backend previewed, and its HTML.
    Kept: Kept option
    /// The count of tab changes. An answer for an older open has a smaller count.
    Opening: int
    HelpShown: bool }

/// `Verified` shows one line with the name and the email address in place of the fields.
type Identity =
  | Verified
  | Fields

type ReplyForm =
  { Thread: string
    Editor: Editor
    Identity: Identity
    Status: Status }

[<RequireQualifiedAccess>]
type Form =
  | Page
  | Reply of thread: string

type Model =
  { /// The URL of the post page, with no query and no fragment.
    PageUrl: string
    /// The validation ID of each email address.
    Validations: Map<string, string>
    List: CommentList
    /// The page form and the reply form share the name and the email address.
    Email: string
    Name: string
    EmailTouched: bool
    Page: Editor
    Status: Status
    Reply: ReplyForm option
    /// The editor of each closed reply form, by its thread.
    Drafts: Map<string, Editor>
    /// The threads of the replies that wait for approval.
    Awaiting: Set<string>
    Arrival: Arrival option
    /// The IDs of the top-level comments that show their earlier replies.
    Expanded: Set<string> }

/// One call for the preview of a source. `Opening` is the count of tab changes at the call, so an answer for an older
/// open has a smaller count than the editor.
type PreviewRequest = { Opening: int; Source: string }

type Edit =
  | TextChanged of string
  | TextLeft
  | WriteSelected
  | PreviewSelected
  | RetrySelected
  | PreviewAnswered of PreviewRequest * ApiResponse<string>
  | PreviewUnanswered of PreviewRequest
  | HelpToggled

type Msg =
  | ListAnswered of ApiResponse<Comment list>
  | ListUnanswered
  | EmailChanged of string
  | NameChanged of string
  | EmailLeft
  | Edited of Form * Edit
  | Submitted of Form
  | ValidationAnswered of Form * ApiResponse<unit>
  | PostAnswered of Form * PostCommentAnswer
  | Unanswered of Form
  | EarlierShown of threadId: string
  | ReplyOpened of threadId: string
  | ReplyCanceled
  | IdentityChanged

type Effect =
  | Load
  | Validate of Form * CommenterValidation
  | Post of Form * NewComment
  | PreviewComment of Form * PreviewRequest
  | ScrollTo of Scroll

type HelpRow = { YouType: string; ReadersSee: string }

/// "Readers see" is the HTML of the comment renderer of the backend for the source.
let helpRows: HelpRow list =
  [ { YouType = "*italic*"; ReadersSee = "<p><em>italic</em></p>\n" }
    { YouType = "**bold**"; ReadersSee = "<p><strong>bold</strong></p>\n" }
    { YouType = "`code`"; ReadersSee = "<p><code>code</code></p>\n" }
    { YouType = "[text](https://example.com)"
      ReadersSee =
        "<p><a href=\"https://example.com\" target=\"_blank\" rel=\"nofollow ugc noopener noreferrer\">text</a></p>\n" }
    { YouType = "- one\n- two"; ReadersSee = "<ul>\n<li>one</li>\n<li>two</li>\n</ul>\n" }
    { YouType = "> a quote"; ReadersSee = "<blockquote>\n<p>a quote</p>\n</blockquote>\n" }
    { YouType = "```fsharp\nlet x = 1\n```"
      ReadersSee =
        "<pre data-lang=\"fsharp\" tabindex=\"0\"><code><span class=\"tk-keyword\">let</span> x "
        + "<span class=\"tk-keyword\">=</span> <span class=\"tk-constant\">1</span>\n</code></pre>\n" } ]

let private find (query: (string * string) list) key =
  query
  |> List.tryFind (fun (name, value) -> name = key && value.Trim() <> "")
  |> Option.map snd

let parse (query: (string * string) list) : Return option =
  let find = find query

  match find "email", find "comment", find validationIdKey with
  | Some email, Some comment, Some validationId ->
    Some
      { Email = email.Trim()
        Name = find "name" |> Option.defaultValue ""
        Comment = comment
        ValidationId = validationId.Trim()
        ReplyTo = find replyToKey |> Option.map _.Trim() }
  | _ -> None

/// A `#comment-<id>` link names this element.
let elementId (id: string) = $"comment-{id}"

let replyFormId (thread: string) = $"reply-{thread}"

let linkedComment (hash: string) : string option =
  let prefix = "#" + elementId ""

  if hash.StartsWith prefix && hash.Length > prefix.Length then
    Some(hash.Substring prefix.Length)
  else
    None

let parseLink (hash: string) (query: (string * string) list) : Link option =
  linkedComment hash
  |> Option.map (fun comment ->
    { Comment = comment
      Thread = find query "thread" |> Option.map _.Trim() })

let emptyEditor =
  { Text = ""
    Touched = false
    Tab = Write
    Kept = None
    Opening = 0
    HelpShown = false }

let private empty (pageUrl: string) (validations: Map<string, string>) =
  { PageUrl = pageUrl
    Validations = validations
    List = Loading
    Email = ""
    Name = ""
    EmailTouched = false
    Page = emptyEditor
    Status = Editing
    Reply = None
    Drafts = Map.empty
    Awaiting = Set.empty
    Arrival = None
    Expanded = Set.empty }

let init
  (pageUrl: string)
  (validations: Map<string, string>)
  (returned: Return option)
  (link: Link option)
  : Model * Effect option =
  let model =
    match returned with
    | Some returned ->
      let filled =
        { empty pageUrl (validations.Add(returned.Email, returned.ValidationId)) with
            Email = returned.Email
            Name = returned.Name }

      match returned.ReplyTo with
      | Some thread ->
        { filled with
            Reply =
              Some
                { Thread = thread
                  Editor = { emptyEditor with Text = returned.Comment }
                  Identity = Verified
                  Status = Validated }
            Arrival = Some(Returned thread) }
      | None ->
        { filled with
            Page = { emptyEditor with Text = returned.Comment }
            Status = Validated
            Arrival = link |> Option.map Linked }
    | None -> { empty pageUrl validations with Arrival = link |> Option.map Linked }

  model, Some Load

/// A reply with no top-level comment in the list has no thread.
let threads (comments: Comment list) : Thread list =
  let sorted = comments |> List.sortBy (_.CreatedDate >> DateTimeOffset.Parse)
  let replies = sorted |> List.filter _.ReplyTo.IsSome

  sorted
  |> List.filter _.ReplyTo.IsNone
  |> List.map (fun comment ->
    { Comment = comment
      Replies = replies |> List.filter (fun reply -> reply.ReplyTo = Some comment.Id) })

let collapseAbove = 3

let newestShown = 2

let private shownThread (expanded: Set<string>) (thread: Thread) : ShownThread =
  let count = thread.Replies.Length

  if count > collapseAbove && not (expanded.Contains thread.Comment.Id) then
    { Comment = thread.Comment
      HiddenEarlier = count - newestShown
      Replies = List.skip (count - newestShown) thread.Replies }
  else
    { Comment = thread.Comment
      HiddenEarlier = 0
      Replies = thread.Replies }

let private shownIn (expanded: Set<string>) (comments: Comment list) =
  comments |> threads |> List.map (shownThread expanded)

let shownThreads (expanded: Set<string>) (list: CommentList) : ShownThread list =
  match list with
  | Loaded comments -> shownIn expanded comments
  | Loading
  | NotLoaded _ -> []

let earlierText (count: int) = $"[show {count} earlier replies]"

/// `author` is the author name of the post, because the backend gives no name for a reply from the author.
let byline (author: string) (comment: Comment) =
  match comment.Writer with
  | Author -> PostAuthor author
  | Commenter -> Reader comment.Commenter

let bylineName (byline: Byline) =
  match byline with
  | Reader name
  | PostAuthor name -> name

let replyToText (author: string) (comment: Comment) =
  $"reply to {bylineName (byline author comment)}"

let private isShown (expanded: Set<string>) (comments: Comment list) (id: string) =
  shownIn expanded comments
  |> List.exists (fun thread -> thread.Comment.Id = id || thread.Replies |> List.exists (fun reply -> reply.Id = id))

let private reveal (link: Link) (comments: Comment list) (expanded: Set<string>) : Set<string> * Scroll =
  let opened =
    comments
    |> List.tryFind (fun comment -> comment.Id = link.Comment)
    |> Option.bind _.ReplyTo
    |> Option.map expanded.Add
    |> Option.defaultValue expanded

  if isShown expanded comments link.Comment then
    expanded, ToComment link.Comment
  elif isShown opened comments link.Comment then
    opened, ToComment link.Comment
  else
    match link.Thread with
    | Some thread when isShown expanded comments thread -> expanded, ToThread thread
    | Some _
    | None -> expanded, ToSection

/// A write tab and a new count make the editor ignore the answer of an open preview.
let private closed (editor: Editor) =
  { editor with Tab = Write; Opening = editor.Opening + 1 }

let private stash (model: Model) =
  match model.Reply with
  | Some reply ->
    { model with
        Reply = None
        Drafts = model.Drafts.Add(reply.Thread, closed reply.Editor) }
  | None -> model

let private listed (list: CommentList) (model: Model) =
  let next = { model with List = list; Arrival = None }

  match model.Arrival, list with
  | None, _ -> next, None
  | Some(Linked link), Loaded comments ->
    let expanded, scroll = reveal link comments model.Expanded
    { next with Expanded = expanded }, Some(ScrollTo scroll)
  | Some(Returned thread), Loaded comments when
    comments |> List.exists (fun comment -> comment.Id = thread && comment.ReplyTo.IsNone)
    ->
    next, Some(ScrollTo(ToReply thread))
  | Some(Returned _), Loaded _ -> stash next, Some(ScrollTo ToSection)
  | Some _, (Loading | NotLoaded _) -> next, Some(ScrollTo ToSection)

let isValidComment (comment: string) = comment.Trim() <> ""

let showsEmailError (model: Model) =
  model.EmailTouched && not (Subscribe.isValidEmail model.Email)

let showsTextError (editor: Editor) =
  editor.Touched && not (isValidComment editor.Text)

let isSending (status: Status) =
  match status with
  | Validating
  | Posting -> true
  | Editing
  | Validated
  | ValidationSent _
  | Posted _
  | Failed _ -> false

/// After a sent validation, the form stays locked until the reader comes back through the link in the email.
let isLocked (status: Status) =
  match status with
  | Validating
  | Posting
  | ValidationSent _ -> true
  | Editing
  | Validated
  | Posted _
  | Failed _ -> false

let isVerified (model: Model) =
  Subscribe.isValidEmail model.Email && model.Validations.ContainsKey(model.Email.Trim())

let shownName (model: Model) =
  match model.Name.Trim() with
  | "" -> "Anonymous"
  | name -> name

/// The backend adds the validation ID to the query of this URL.
let redirectUri (model: Model) (text: string) (replyTo: string option) =
  let name = model.Name.Trim()

  let query =
    [ "email", model.Email.Trim()
      if name <> "" then
        "name", name
      "comment", text.Trim()
      match replyTo with
      | Some thread -> replyToKey, thread
      | None -> () ]
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

let replyStatusText (status: Status) =
  match status with
  | Failed reason -> reason
  | Validated -> "Email verified. Click [post reply] to post your reply."
  | ValidationSent email -> $"Check your inbox. We sent a verification link to {email}. Open it, then post your reply."
  | Editing
  | Validating
  | Posting
  | Posted _ -> ""

let awaitingText = "Thanks! Your reply will appear here once it's approved."

let private listFailed = "Couldn't load comments. Try again later."

let private unanswered = "Couldn't reach the server. Try again later."

let private refused (form: Form) (message: string option) =
  let fallback =
    match form with
    | Form.Page -> "Couldn't post your comment. Try again later."
    | Form.Reply _ -> "Couldn't post your reply. Try again later."

  Failed(message |> Option.defaultValue fallback)

let private posted (answer: PostCommentAnswer) (list: CommentList) =
  match answer.Comment, list with
  | Some { Comment = comment; Approval = Approved }, Loaded comments -> Approved, Loaded(comments @ [ comment ])
  | Some { Approval = Approved }, (Loading | NotLoaded _) -> Approved, list
  | Some { Approval = AwaitsApproval }, _
  | None, _ -> AwaitsApproval, list

let private send (model: Model) (form: Form) (text: string) =
  let email = model.Email.Trim()

  let replyTo =
    match form with
    | Form.Page -> None
    | Form.Reply thread -> Some thread

  match model.Validations.TryFind email with
  | Some validationId ->
    Posting,
    Post(
      form,
      { ValidationId = validationId
        Comment = text.Trim()
        ReplyTo = replyTo }
    )
  | None ->
    let name = model.Name.Trim()

    Validating,
    Validate(
      form,
      { EmailAddress = email
        Name = if name = "" then None else Some name
        RedirectUri = redirectUri model text replyTo }
    )

let private openPreview (editor: Editor) =
  let opening = editor.Opening + 1
  let source = editor.Text.Trim()

  match editor.Kept with
  | _ when source = "" -> { editor with Tab = Preview PreviewState.Empty; Opening = opening }, None
  | Some kept when kept.Source = source -> { editor with Tab = Preview(PreviewState.Shown kept.Html); Opening = opening }, None
  | _ ->
    { editor with Tab = Preview PreviewState.Loading; Opening = opening },
    Some { Opening = opening; Source = source }

let private isAwaited (request: PreviewRequest) (editor: Editor) =
  request.Opening = editor.Opening && editor.Tab = Preview PreviewState.Loading

let private previewed (request: PreviewRequest) (response: ApiResponse<string>) (editor: Editor) =
  match response.Success, response.Data with
  | true, Some html ->
    { editor with
        Tab = Preview(PreviewState.Shown html)
        Kept = Some { Source = request.Source; Html = html } }
  | _ -> { editor with Tab = Preview PreviewState.Failed }

let private edit (edit: Edit) (editor: Editor) : Editor * PreviewRequest option =
  match edit with
  | TextChanged text -> { editor with Text = text }, None
  | TextLeft -> { editor with Touched = editor.Touched || editor.Text <> "" }, None
  | WriteSelected when editor.Tab = Write -> editor, None
  | WriteSelected -> { editor with Tab = Write; Opening = editor.Opening + 1 }, None
  | PreviewSelected when editor.Tab = Write -> openPreview editor
  | PreviewSelected -> editor, None
  | RetrySelected when editor.Tab = Preview PreviewState.Failed -> openPreview editor
  | RetrySelected -> editor, None
  | PreviewAnswered(request, response) when isAwaited request editor -> previewed request response editor, None
  | PreviewUnanswered request when isAwaited request editor -> { editor with Tab = Preview PreviewState.Failed }, None
  | PreviewAnswered _
  | PreviewUnanswered _ -> editor, None
  | HelpToggled -> { editor with HelpShown = not editor.HelpShown }, None

/// A message for a reply form that is closed changes nothing.
let private withReply (thread: string) (change: ReplyForm -> Model * Effect option) (model: Model) =
  match model.Reply with
  | Some reply when reply.Thread = thread -> change reply
  | Some _
  | None -> model, None

let private edited (form: Form) (change: Edit) (model: Model) =
  match form with
  | Form.Page ->
    let editor, request = edit change model.Page
    { model with Page = editor }, request |> Option.map (fun request -> PreviewComment(form, request))
  | Form.Reply thread ->
    model
    |> withReply thread (fun reply ->
      let editor, request = edit change reply.Editor

      { model with Reply = Some { reply with Editor = editor } },
      request |> Option.map (fun request -> PreviewComment(form, request)))

let private submitted (form: Form) (model: Model) =
  match form with
  | Form.Page when isLocked model.Status -> model, None
  | Form.Page when not (Subscribe.isValidEmail model.Email && isValidComment model.Page.Text) ->
    { model with
        EmailTouched = true
        Page = { model.Page with Touched = true } },
    None
  | Form.Page ->
    let status, effect = send model form model.Page.Text
    { model with Status = status }, Some effect
  | Form.Reply thread ->
    model
    |> withReply thread (fun reply ->
      if isLocked reply.Status then
        model, None
      elif not (Subscribe.isValidEmail model.Email && isValidComment reply.Editor.Text) then
        { model with
            EmailTouched = true
            Reply = Some { reply with Editor = { reply.Editor with Touched = true } } },
        None
      else
        let status, effect = send model form reply.Editor.Text
        { model with Reply = Some { reply with Status = status } }, Some effect)

let private withStatus (form: Form) (status: Status) (model: Model) =
  match form with
  | Form.Page -> { model with Status = status }, None
  | Form.Reply thread -> model |> withReply thread (fun reply -> { model with Reply = Some { reply with Status = status } }, None)

let private postAnswered (form: Form) (answer: PostCommentAnswer) (model: Model) =
  let approval, list = posted answer model.List

  match form with
  | Form.Page ->
    { model with
        List = list
        Status = Posted approval
        Page =
          { emptyEditor with
              Opening = model.Page.Opening
              HelpShown = model.Page.HelpShown } },
    None
  | Form.Reply thread ->
    { model with
        List = list
        Reply = model.Reply |> Option.filter (fun reply -> reply.Thread <> thread)
        Drafts = model.Drafts.Remove thread
        Awaiting =
          match approval with
          | Approved -> model.Awaiting.Remove thread
          | AwaitsApproval -> model.Awaiting.Add thread },
    None

let private isReplySending (model: Model) =
  model.Reply |> Option.exists (fun reply -> isSending reply.Status)

let private replyOpened (thread: string) (model: Model) =
  if isReplySending model || model.Reply |> Option.exists (fun reply -> reply.Thread = thread) then
    model, None
  else
    let next = stash model

    { next with
        Reply =
          Some
            { Thread = thread
              Editor = next.Drafts.TryFind thread |> Option.defaultValue emptyEditor
              Identity = if isVerified model then Verified else Fields
              Status = Editing }
        Drafts = next.Drafts.Remove thread
        Awaiting = next.Awaiting.Remove thread },
    None

let update (msg: Msg) (model: Model) : Model * Effect option =
  match msg with
  | ListAnswered response when response.Success -> listed (Loaded(response.Data |> Option.defaultValue [])) model
  | ListAnswered response -> listed (NotLoaded(response.Message |> Option.defaultValue listFailed)) model
  | ListUnanswered -> listed (NotLoaded unanswered) model
  | EmailChanged email -> { model with Email = email }, None
  | NameChanged name -> { model with Name = name }, None
  | EmailLeft -> { model with EmailTouched = model.EmailTouched || model.Email.Trim() <> "" }, None
  | Edited(form, change) -> edited form change model
  | Submitted form -> submitted form model
  | ValidationAnswered(form, response) when response.Success -> withStatus form (ValidationSent(model.Email.Trim())) model
  | ValidationAnswered(form, response) -> withStatus form (refused form response.Message) model
  | PostAnswered(form, answer) when answer.Success -> postAnswered form answer model
  | PostAnswered(form, answer) -> withStatus form (refused form answer.Message) model
  | Unanswered form -> withStatus form (Failed unanswered) model
  | EarlierShown threadId -> { model with Expanded = model.Expanded.Add threadId }, None
  | ReplyOpened thread -> replyOpened thread model
  | ReplyCanceled when isReplySending model -> model, None
  | ReplyCanceled -> stash model, None
  | IdentityChanged ->
    { model with Reply = model.Reply |> Option.map (fun reply -> { reply with Identity = Fields }) }, None
