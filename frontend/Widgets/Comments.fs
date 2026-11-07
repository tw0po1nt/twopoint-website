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

/// The `thread` query of a reply email names the top-level comment, for a reply that is gone.
type Link = { Comment: string; Thread: string option }

type Scroll =
  | ToComment of id: string
  | ToThread of id: string
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
    Status: Status
    Tab: Tab
    /// The last source that the backend previewed, and its HTML.
    Kept: Kept option
    /// The count of tab changes. An answer for an older open has a smaller count.
    Opening: int
    HelpShown: bool
    /// The link of the URL, until the comments load.
    Link: Link option
    /// The IDs of the top-level comments that show their earlier replies.
    Expanded: Set<string> }

/// One call for the preview of a source. `Opening` is the count of tab changes at the call, so an answer for an older
/// open has a smaller count than the model.
type PreviewRequest = { Opening: int; Source: string }

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
  | PostAnswered of PostCommentAnswer
  | Unanswered
  | WriteSelected
  | PreviewSelected
  | RetrySelected
  | PreviewAnswered of PreviewRequest * ApiResponse<string>
  | PreviewUnanswered of PreviewRequest
  | HelpToggled
  | EarlierShown of threadId: string

type Effect =
  | Load
  | Validate of CommenterValidation
  | Post of NewComment
  | PreviewComment of PreviewRequest
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

/// A `#comment-<id>` link names this element.
let elementId (id: string) = $"comment-{id}"

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
      Thread =
        query
        |> List.tryFind (fun (name, value) -> name = "thread" && value.Trim() <> "")
        |> Option.map (snd >> _.Trim()) })

let private empty (pageUrl: string) (validations: Map<string, string>) =
  { PageUrl = pageUrl
    Validations = validations
    List = Loading
    Email = ""
    Name = ""
    Comment = ""
    EmailTouched = false
    CommentTouched = false
    Status = Editing
    Tab = Write
    Kept = None
    Opening = 0
    HelpShown = false
    Link = None
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
      { empty pageUrl (validations.Add(returned.Email, returned.ValidationId)) with
          Email = returned.Email
          Name = returned.Name
          Comment = returned.Comment
          Status = Validated }
    | None -> empty pageUrl validations

  { model with Link = link }, Some Load

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

let private listed (list: CommentList) (model: Model) =
  let next = { model with List = list; Link = None }

  match model.Link, list with
  | None, _ -> next, None
  | Some link, Loaded comments ->
    let expanded, scroll = reveal link comments model.Expanded
    { next with Expanded = expanded }, Some(ScrollTo scroll)
  | Some _, (Loading | NotLoaded _) -> next, Some(ScrollTo ToSection)

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
  | Posted _
  | Failed _ -> false

/// After a sent validation, the form stays locked until the reader comes back through the link in the email.
let isLocked (model: Model) =
  match model.Status with
  | Validating
  | Posting
  | ValidationSent _ -> true
  | Editing
  | Validated
  | Posted _
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

let private listFailed = "Couldn't load comments. Try again later."

let private refused = "Couldn't post your comment. Try again later."

let private unanswered = "Couldn't reach the server. Try again later."

let private failed (message: string option) =
  Failed(message |> Option.defaultValue refused)

let private posted (answer: PostCommentAnswer) (list: CommentList) =
  match answer.Comment, list with
  | Some { Comment = comment; Approval = Approved }, Loaded comments -> Approved, Loaded(comments @ [ comment ])
  | Some { Approval = Approved }, (Loading | NotLoaded _) -> Approved, list
  | Some { Approval = AwaitsApproval }, _
  | None, _ -> AwaitsApproval, list

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

let private openPreview (model: Model) =
  let opening = model.Opening + 1
  let source = model.Comment.Trim()

  match model.Kept with
  | _ when source = "" -> { model with Tab = Preview PreviewState.Empty; Opening = opening }, None
  | Some kept when kept.Source = source -> { model with Tab = Preview(PreviewState.Shown kept.Html); Opening = opening }, None
  | _ ->
    { model with Tab = Preview PreviewState.Loading; Opening = opening },
    Some(PreviewComment { Opening = opening; Source = source })

let private isAwaited (request: PreviewRequest) (model: Model) =
  request.Opening = model.Opening && model.Tab = Preview PreviewState.Loading

let private previewed (request: PreviewRequest) (response: ApiResponse<string>) (model: Model) =
  match response.Success, response.Data with
  | true, Some html ->
    { model with
        Tab = Preview(PreviewState.Shown html)
        Kept = Some { Source = request.Source; Html = html } }
  | _ -> { model with Tab = Preview PreviewState.Failed }

let update (msg: Msg) (model: Model) : Model * Effect option =
  match msg with
  | ListAnswered response when response.Success -> listed (Loaded(response.Data |> Option.defaultValue [])) model
  | ListAnswered response -> listed (NotLoaded(response.Message |> Option.defaultValue listFailed)) model
  | ListUnanswered -> listed (NotLoaded unanswered) model
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
  | ValidationAnswered response -> { model with Status = failed response.Message }, None
  | PostAnswered answer when answer.Success ->
    let approval, list = posted answer model.List

    { empty model.PageUrl model.Validations with
        List = list
        Status = Posted approval
        Opening = model.Opening
        HelpShown = model.HelpShown
        Expanded = model.Expanded },
    None
  | PostAnswered answer -> { model with Status = failed answer.Message }, None
  | Unanswered -> { model with Status = Failed unanswered }, None
  | WriteSelected when model.Tab = Write -> model, None
  | WriteSelected -> { model with Tab = Write; Opening = model.Opening + 1 }, None
  | PreviewSelected when model.Tab = Write -> openPreview model
  | PreviewSelected -> model, None
  | RetrySelected when model.Tab = Preview PreviewState.Failed -> openPreview model
  | RetrySelected -> model, None
  | PreviewAnswered(request, response) when isAwaited request model -> previewed request response model, None
  | PreviewUnanswered request when isAwaited request model -> { model with Tab = Preview PreviewState.Failed }, None
  | PreviewAnswered _
  | PreviewUnanswered _ -> model, None
  | HelpToggled -> { model with HelpShown = not model.HelpShown }, None
  | EarlierShown threadId -> { model with Expanded = model.Expanded.Add threadId }, None
