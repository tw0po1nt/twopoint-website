namespace TwoPoint.Core.Posts

open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

open System
open System.Net
open System.Text.RegularExpressions

[<RequireQualifiedAccess>]
type ReplyEmailRecord =
  | NotSent
  | Sent of sentDate : DateTime

[<RequireQualifiedAccess>]
type ReplyEmailSkip =
  | ReplyMissing
  | NotAReply
  | ReplyNotApproved
  | AlreadySent
  | TopLevelMissing
  | TopLevelNotApproved
  | NoConsent
  | Stopped
  | RecipientBanned
  | OwnReply
  | PostNotPublished

type ReplyEmailSend =
  { Post : Slug
    PostTitle : NonEmptyString
    Reply : Comment
    TopLevel : Comment
    Recipient : Commenter
    StopToken : ReplyEmailStopToken }

[<RequireQualifiedAccess>]
module ReplyEmailRule =

  /// `before` is None for a comment that its writer posts as approved.
  let isFirstApproval (before : CommentApproval option) (comment : Comment) =
    match comment.Parent, before, comment.Status.Approval with
    | Some _, (None | Some CommentApproval.New), CommentApproval.Approved -> true
    | None, _, _
    | Some _, Some (CommentApproval.Approved | CommentApproval.Rejected), _
    | Some _, (None | Some CommentApproval.New), (CommentApproval.New | CommentApproval.Rejected) -> false

  let private isApproved (comment : Comment) = comment.Status.Approval = CommentApproval.Approved

  let private recipientOf (topLevel : Comment) : Result<Commenter * ReplyEmailConsent, ReplyEmailSkip> =
    match topLevel.ReplyEmails, Comment.commenter topLevel with
    | None, _
    | _, None -> Error ReplyEmailSkip.NoConsent
    | Some { StopDate = Some _ }, Some _ -> Error ReplyEmailSkip.Stopped
    | Some _, Some recipient when recipient.Status = CommenterStatus.Banned -> Error ReplyEmailSkip.RecipientBanned
    | Some consent, Some recipient -> Ok (recipient, consent)

  /// Give the top-level comment of the thread. A reply always gives false.
  let getsReplyEmails (topLevel : Comment) =
    match topLevel.Parent with
    | Some _ -> false
    | None -> recipientOf topLevel |> Result.isOk

  let private isWrittenBy (recipient : Commenter) (comment : Comment) =
    Comment.commenter comment |> Option.exists (fun writer -> writer.ValidationId = recipient.ValidationId)

  /// The gate at the send. `topLevel` is the comment that the parent of `reply` names.
  let check
    (reply : PostedComment option)
    (topLevel : PostedComment option)
    (record : ReplyEmailRecord)
    : Result<ReplyEmailSend, ReplyEmailSkip> =
    match reply with
    | None -> Error ReplyEmailSkip.ReplyMissing
    | Some { Comment = { Parent = None } } -> Error ReplyEmailSkip.NotAReply
    | Some { Comment = reply } when not (isApproved reply) -> Error ReplyEmailSkip.ReplyNotApproved
    | Some _ when record <> ReplyEmailRecord.NotSent -> Error ReplyEmailSkip.AlreadySent
    | Some posted ->
      match topLevel with
      | Some topLevel when topLevel.Post = posted.Post && Some topLevel.Comment.Id = posted.Comment.Parent ->
        match isApproved topLevel.Comment, recipientOf topLevel.Comment with
        | false, _ -> Error ReplyEmailSkip.TopLevelNotApproved
        | true, Error skip -> Error skip
        | true, Ok (recipient, _) when isWrittenBy recipient posted.Comment -> Error ReplyEmailSkip.OwnReply
        | true, Ok (recipient, consent) ->
          Ok
            { Post = posted.Post
              PostTitle = posted.PostTitle
              Reply = posted.Comment
              TopLevel = topLevel.Comment
              Recipient = recipient
              StopToken = consent.StopToken }
      | Some _
      | None -> Error ReplyEmailSkip.TopLevelMissing

[<RequireQualifiedAccess>]
module QueueReplyEmail =

  let atFirstApproval
    (queue : Slug -> CommentId -> DependencyResult<unit>)
    (post : Slug)
    (before : CommentApproval option)
    (comment : Comment)
    : DependencyResult<unit> =
    if ReplyEmailRule.isFirstApproval before comment then
      queue post comment.Id |> CancellableTask.map (fun _ -> Ok ())
    else
      CancellableTaskResult.singleton ()


type ReplyEmailContent =
  { Subject : string
    Html : string
    Text : string }

[<RequireQualifiedAccess>]
module ReplyEmailContent =

  let postUrl (site : Uri) (post : Slug) = Uri(site, $"/posts/{Slug.value post}/")

  /// The widget scrolls to the top-level comment of the query when the reply is gone.
  let threadUrl (site : Uri) (send : ReplyEmailSend) =
    Uri(postUrl site send.Post, $"?thread={send.TopLevel.Id}#comment-{send.Reply.Id}")

  let stopUrl (site : Uri) (token : ReplyEmailStopToken) = Uri(site, $"/comments/stop/?token={token}")

  let private replierName (authorName : string) (reply : Comment) =
    match reply.Writer with
    | CommentWriter.Commenter commenter -> Commenter.displayName commenter
    | CommentWriter.Author -> authorName

  let private recipientName (recipient : Commenter) =
    recipient.Name |> Option.map NonEmptyString.value |> Option.defaultValue "there"

  let private paragraph = "margin: 0 0 16px; font-size: 16px; line-height: 24px; color: #475569"

  let private codeFont = "ui-monospace, Menlo, Consolas, monospace"

  /// The comment renderer escapes the text of a comment, so each tag here comes from the renderer.
  let private styles =
    [ "<p>", $"<p style=\"{paragraph}\">"
      "<code>", $"<code style=\"font-family: {codeFont}; font-size: 14px; background-color: #f1f5f9; border-radius: 4px; padding: 2px 4px\">"
      "<pre ", $"<pre style=\"margin: 0 0 16px; padding: 12px 16px; overflow-x: auto; background-color: #f1f5f9; border-radius: 4px; font-family: {codeFont}; font-size: 14px; line-height: 20px; color: #0f172a\" "
      "<blockquote>", "<blockquote style=\"margin: 0 0 16px; padding: 0 0 0 16px; border-left: 4px solid #cbd5e1\">"
      "<ul>", "<ul style=\"margin: 0 0 16px; padding: 0 0 0 24px; font-size: 16px; line-height: 24px; color: #475569\">"
      "<ol>", "<ol style=\"margin: 0 0 16px; padding: 0 0 0 24px; font-size: 16px; line-height: 24px; color: #475569\">"
      "<li>", "<li style=\"margin: 0 0 4px\">" ]

  let private styleTags = Regex(styles |> List.map (fst >> Regex.Escape) |> String.concat "|")

  let private styledTags = Map.ofList styles

  /// Inline styles for the HTML of the comment renderer, because many mail clients drop a style sheet.
  let styled (html : string) = styleTags.Replace(html, fun found -> styledTags[found.Value])

  let private encode (text : string) = WebUtility.HtmlEncode text

  /// `sentDate` gives the year of the copyright line.
  let compose
    (stage : TwoPoint.Site.Render.Stage)
    (site : Uri)
    (authorName : string)
    (sentDate : DateTime)
    (send : ReplyEmailSend)
    : ReplyEmailContent =
    let name = replierName authorName send.Reply
    let title = NonEmptyString.value send.PostTitle
    let postUrl = (postUrl site send.Post).AbsoluteUri
    let threadUrl = (threadUrl site send).AbsoluteUri
    let stopUrl = (stopUrl site send.StopToken).AbsoluteUri
    let excerpt = AdminComment.excerpt send.TopLevel
    let reply = NonEmptyString.value send.Reply.Content

    let byline =
      match send.Reply.Writer with
      | CommentWriter.Commenter _ -> name
      | CommentWriter.Author -> $"{name} (author)"

    let copyright = $"© {sentDate.Year} TwoPoint. All rights reserved."

    let html =
      $$"""<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="x-apple-disable-message-reformatting">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="format-detection" content="telephone=no, date=no, address=no, email=no, url=no">
  <meta name="color-scheme" content="light dark">
  <meta name="supported-color-schemes" content="light dark">
  <link rel="preconnect" href="https://fonts.googleapis.com">
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
  <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;600&display=swap" rel="stylesheet" media="screen">
  <style>
    .hover-bg-slate-800:hover {
      background-color: #1e293b !important
    }
    @media (max-width: 600px) {
      .sm-p-6 {
        padding: 24px !important
      }
      .sm-px-4 {
        padding-left: 16px !important;
        padding-right: 16px !important
      }
      .sm-px-6 {
        padding-left: 24px !important;
        padding-right: 24px !important
      }
    }
    @media (prefers-color-scheme: dark) {
      .dark-text-red-500 {
        color: #ef4444 !important
      }
      .dark-text-white {
        color: #fffffe !important
      }
    }
  </style>
</head>
<body style="margin: 0; width: 100%; background-color: #f8fafc; padding: 0; -webkit-font-smoothing: antialiased; word-break: break-word">
  <div role="article" aria-roledescription="email" aria-label="{{encode name}} replied to your comment" lang="en">
    <div class="sm-px-4" style="background-color: #fafafa; font-family: Inter, ui-sans-serif, system-ui, -apple-system, 'Segoe UI', sans-serif">
      <table align="center" style="margin: 0 auto" cellpadding="0" cellspacing="0" role="none">
        <tr>
          <td style="width: 552px; max-width: 100%">
            <div role="separator" style="line-height: 24px">&zwj;</div>
            <table style="width: 100%" cellpadding="0" cellspacing="0" role="none">
              <tr>
                <td class="sm-p-6" style="border-radius: 8px; background-color: #fffffe; padding: 24px 36px; border: 1px solid #e2e8f0">
                  <a href="{{encode site.AbsoluteUri}}" style="color: #000001; text-decoration: none">
                    <span class="dark-text-white" style="display: flex; flex-direction: row; align-items: center; font-family: {{codeFont}}; font-size: 20px; font-weight: 700">
                      Two&nbsp;
                      <svg class="dark-text-red-500" height="1em" width="1em" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg" style="color: #dc2626"><path d="m7 8l-4 4l4 4m10-8l4 4l-4 4M14 4l-4 16" fill="none" stroke="currentColor" stroke-linecap="round" stroke-linejoin="round" stroke-width="2"></path></svg>
                      &nbsp;Point
                    </span>
                  </a>
                  <div role="separator" style="line-height: 24px">&zwj;</div>
                  <h1 style="margin: 0 0 24px; font-size: 24px; line-height: 32px; font-weight: 600; color: #0f172a">
                    Hey {{encode (recipientName send.Recipient)}}!
                  </h1>
                  <p style="{{paragraph}}">
                    {{encode byline}} replied to your comment on <a href="{{encode postUrl}}" style="color: #1e293b; text-decoration: underline">{{encode title}}</a>:
                  </p>
                  <blockquote style="margin: 0 0 24px; padding: 0 0 0 16px; border-left: 4px solid #cbd5e1; font-size: 14px; line-height: 22px; color: #64748b">
                    {{encode excerpt}}
                  </blockquote>
                  <div>
{{styled (TwoPoint.Site.Render.CommentBody.html stage reply)}}
                  </div>
                  <div role="separator" style="line-height: 8px">&zwj;</div>
                  <div>
                    <a href="{{encode threadUrl}}" style="display: inline-block; text-decoration: none; padding: 16px 24px; font-size: 16px; line-height: 1; border-radius: 4px; color: #fffffe; background-color: #020617" class="hover-bg-slate-800">
                      <!--[if mso]><i style="mso-font-width: 150%; mso-text-raise: 31px" hidden>&emsp;</i><![endif]-->
                      <span style="mso-text-raise: 16px">Read the thread</span>
                      <!--[if mso]><i hidden style="mso-font-width: 150%">&emsp;&#8203;</i><![endif]-->
                    </a>
                  </div>
                  <p style="margin: 8px 0 0; font-size: 14px; line-height: 20px; color: #64748b">
                    Reply on the post
                  </p>
                  <div role="separator" style="height: 1px; line-height: 1px; background-color: #cbd5e1; margin-top: 24px; margin-bottom: 24px">&zwj;</div>
                  <p style="margin: 0; font-size: 12px; line-height: 20px; color: #475569">
                    You asked for reply emails when you commented on {{encode title}}.
                    <a href="{{encode stopUrl}}" style="color: #1e293b; text-decoration: underline">Stop reply emails for this comment</a>
                  </p>
                </td>
              </tr>
            </table>
            <table style="width: 100%" cellpadding="0" cellspacing="0" role="none">
              <tr>
                <td class="sm-px-6" style="padding: 24px 36px">
                  <p style="margin: 0; font-size: 12px; color: #64748b">
                    {{encode copyright}}
                  </p>
                </td>
              </tr>
            </table>
          </td>
        </tr>
      </table>
    </div>
  </div>
</body>
</html>
"""

    let text =
      String.concat
        "\n"
        [ $"Hey {recipientName send.Recipient}!"
          ""
          $"{byline} replied to your comment on {title}:"
          ""
          $"> {excerpt}"
          ""
          TwoPoint.Site.Render.CommentBody.plainText reply
          ""
          $"Read the thread: {threadUrl}"
          "Reply on the post"
          ""
          "---"
          $"You asked for reply emails when you commented on {title}."
          $"Stop reply emails for this comment: {stopUrl}"
          ""
          copyright
          "" ]

    { Subject = $"{name} replied to your comment on {title}"
      Html = html
      Text = text }


namespace TwoPoint.Core.Posts.Actions

open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

open System

[<RequireQualifiedAccess>]
module SendReplyEmail =

  type Dto = { Post : string; ReplyId : string }

  type Error = unit

  type Event =
    | Sent of post : Slug * reply : CommentId
    /// ACS took the email, so a retry must not send it again.
    | SentWithNoRecord of post : Slug * reply : CommentId * error : DependencyError
    | Skipped of reply : CommentId * reason : ReplyEmailSkip

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetPostedComment: post: Slug -> commentId: CommentId -> DependencyResult<PostedComment option>
    abstract member GetReplyEmailRecord: post: Slug -> reply: CommentId -> DependencyResult<ReplyEmailRecord>
    abstract member GetPublishedBlob: slug: Slug -> DependencyResult<StoredBlob option>
    abstract member SendReplyEmail: recipient: EmailAddress -> content: ReplyEmailContent -> DependencyResult<unit>
    abstract member RecordReplyEmailSent: post: Slug -> reply: CommentId -> sentDate: DateTime -> DependencyResult<unit>
    abstract member GetUtcNow: unit -> DateTime
    /// The root URL of the site that the email links to.
    abstract member Site: Uri
    abstract member Stage: TwoPoint.Site.Render.Stage

  /// A post with no author in its front matter fails the check of the build, so the fallback is for a broken blob.
  let private authorOf (blob : StoredBlob) =
    (TwoPoint.Site.Render.PostText.details blob.Text).Author |> Option.defaultValue "The author"

  let withDependencies (dependencies : Dependencies) : Action =

    let send (gated : ReplyEmailSend) : DependencyResult<Event> =
      cancellableTaskResult {
        match! dependencies.GetPublishedBlob gated.Post with
        | None -> return Skipped (gated.Reply.Id, ReplyEmailSkip.PostNotPublished)
        | Some blob ->
          let sentDate = dependencies.GetUtcNow()
          let content = ReplyEmailContent.compose dependencies.Stage dependencies.Site (authorOf blob) sentDate gated
          do! dependencies.SendReplyEmail gated.Recipient.EmailAddress content

          let! recorded = dependencies.RecordReplyEmailSent gated.Post gated.Reply.Id sentDate |> CancellableTask.map Ok

          match recorded with
          | Ok () -> return Sent (gated.Post, gated.Reply.Id)
          | Error error -> return SentWithNoRecord (gated.Post, gated.Reply.Id, error)
      }

    fun dto -> cancellableTaskResult {
      let! post, replyId =
        validation {
          let! post = Slug.create dto.Post
          and! replyId = CommentId.create dto.ReplyId
          return post, replyId
        }
        |> Result.mapError ActionError<Error>.Validation

      let! (reply : PostedComment option), (topLevel : PostedComment option), record =
        cancellableTaskResult {
          match! dependencies.GetPostedComment post replyId with
          | None -> return None, None, ReplyEmailRecord.NotSent
          | Some (posted : PostedComment) ->
            let! topLevel = posted.Comment.Parent |> Option.traverseCancellableTaskResult (dependencies.GetPostedComment posted.Post)
            let! record = dependencies.GetReplyEmailRecord posted.Post posted.Comment.Id
            return Some posted, Option.flatten topLevel, record
        }
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      match ReplyEmailRule.check reply topLevel record with
      | Error reason -> return Skipped (replyId, reason)
      | Ok gated -> return! send gated |> CancellableTaskResult.mapError ActionError<Error>.Dependency
    }
