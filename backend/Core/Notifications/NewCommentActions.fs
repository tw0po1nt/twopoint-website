namespace TwoPoint.Core.Notifications.Actions

open TwoPoint.Core.Notifications
open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

open System
open System.Text
open System.Text.Encodings.Web
open System.Text.Json

type NewCommentCase =
  | AwaitsModeration
  | Approved

type NewCommentPayload =
  { Case : NewCommentCase
    CommentId : string
    Slug : string
    Title : string
    Body : string
    NewCount : int }

[<RequireQualifiedAccess>]
module NewCommentPayload =

  /// A push service takes 4096 octets. The sender takes 3993 octets of plaintext at most.
  let maxOctets = 3993

  // JSON escapes a character outside the BMP as 12 octets, so these three limits keep the payload below `maxOctets`.
  let maxTitleLength = 100
  let maxCommenterLength = 60
  /// The notification shows the first 120 characters of the plain text of the comment.
  let maxTextLength = 120

  let private truncate (length : int) (text : string) =
    text.EnumerateRunes() |> Seq.truncate length |> Seq.map _.ToString() |> String.concat ""

  let private textOf (plainText : string) =
    if String.IsNullOrWhiteSpace plainText then "(no text)" else plainText

  let private caseName =
    function
    | AwaitsModeration -> "awaits-moderation"
    | Approved -> "approved"

  let private nameOf (commenter : Commenter) =
    commenter.Name
    |> Option.map NonEmptyString.value
    |> Option.defaultValue (string commenter.EmailAddress)
    |> truncate maxCommenterLength

  /// `parent` is the top-level comment of a reply, and None for a top-level comment or a parent that is gone.
  let ofComment (newCount : int) (parent : Comment option) (posted : PostedComment) : NewCommentPayload option =
    let comment = posted.Comment

    let payload case =
      comment
      |> Comment.commenter
      |> Option.map (fun commenter ->
        let name = nameOf commenter
        let text = comment.Content |> NonEmptyString.value |> TwoPoint.Site.Render.CommentBody.plainText |> textOf |> truncate maxTextLength
        let postTitle = posted.PostTitle |> NonEmptyString.value |> truncate maxTitleLength

        let title, body =
          match comment.Parent with
          | Some _ ->
            let parentName = parent |> Option.bind Comment.commenter |> Option.map nameOf |> Option.defaultValue "a comment"
            $"{name} replied to {parentName}", text
          | None ->
            match case with
            | AwaitsModeration -> $"New comment on {postTitle}", $"{name}: {text}"
            | Approved -> $"Comment posted on {postTitle}", $"{name}: {text}"

        { Case = case
          CommentId = string comment.Id
          Slug = Slug.value posted.Post
          Title = title
          Body = body
          NewCount = newCount })

    match comment.Status.Approval with
    | CommentApproval.New -> payload AwaitsModeration
    | CommentApproval.Approved -> payload Approved
    | CommentApproval.Rejected -> None

  // The relaxed encoder keeps a character as UTF-8 and does not escape it as 6 octets.
  let private options = JsonSerializerOptions(Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

  let toBytes (payload : NewCommentPayload) : byte array =
    JsonSerializer.SerializeToUtf8Bytes(
      {| ``case`` = caseName payload.Case
         commentId = payload.CommentId
         slug = payload.Slug
         title = payload.Title
         body = payload.Body
         newCount = payload.NewCount |},
      options
    )

[<RequireQualifiedAccess>]
module NotifyNewComment =

  type Dto = { CommentId : string }

  type Error = unit

  type Event =
    | Notified of Pushed
    | Skipped of CommentId

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetPostedComment: CommentId -> DependencyResult<PostedComment option>
    abstract member CountNewComments: unit -> DependencyResult<int>
    abstract member GetDevicesOfAuthor: unit -> DependencyResult<RegisteredDevice list>
    abstract member SendPush: RegisteredDevice -> payload: byte array -> DependencyResult<PushOutcome>
    abstract member RecordPush: RegisteredDevice -> CommentId -> DependencyResult<unit>
    abstract member DeleteDevice: RegisteredDevice -> DependencyResult<unit>

  let withDependencies (dependencies : Dependencies) : Action =
    fun dto -> cancellableTaskResult {
      let! commentId =
        CommentId.create dto.CommentId
        |> Result.mapError ActionError<Error>.Validation

      let! posted =
        dependencies.GetPostedComment commentId
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      match posted with
      | None -> return Skipped commentId
      | Some (posted : PostedComment) ->
        let! newCount =
          dependencies.CountNewComments()
          |> CancellableTaskResult.mapError ActionError<Error>.Dependency

        let! parent =
          posted.Comment.Parent
          |> Option.traverseCancellableTaskResult dependencies.GetPostedComment
          |> CancellableTaskResult.map (Option.flatten >> Option.map (fun (parent : PostedComment) -> parent.Comment))
          |> CancellableTaskResult.mapError ActionError<Error>.Dependency

        match NewCommentPayload.ofComment newCount parent posted with
        | None -> return Skipped commentId
        | Some payload ->
          let! devices =
            dependencies.GetDevicesOfAuthor()
            |> CancellableTaskResult.mapError ActionError<Error>.Dependency

          let bytes = NewCommentPayload.toBytes payload

          let! pushed =
            devices
            |> DevicePush.pushAll
              (fun device -> dependencies.SendPush device bytes)
              (fun device -> dependencies.RecordPush device commentId)
              dependencies.DeleteDevice
            |> CancellableTask.map Ok

          return Notified pushed
    }
