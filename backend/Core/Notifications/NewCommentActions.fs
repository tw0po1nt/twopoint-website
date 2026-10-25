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

/// What the push worker of the admin gets for a new comment. The worker builds the notification.
type NewCommentCase =
  /// The comment waits for moderation.
  | AwaitsModeration
  /// A trusted commenter wrote the comment, and it is approved.
  | Approved

type PushOutcome =
  | Delivered
  /// The push service answered `404` or `410`.
  | DeviceGone

type NewCommentPayload =
  { Case : NewCommentCase
    CommentId : string
    Slug : string
    PostTitle : string
    Commenter : string
    Text : string
    NewCount : int }

[<RequireQualifiedAccess>]
module NewCommentPayload =

  /// A push service takes 4096 octets. The sender takes 3993 octets of plaintext at most.
  let maxOctets = 3993

  // The limits keep the payload below `maxOctets`. JSON escapes a character outside the BMP as 12 octets, and 280 of them fit.
  let maxTitleLength = 100
  let maxCommenterLength = 60
  /// The notification shows the first 120 characters of the text.
  let maxTextLength = 120

  let private truncate (length : int) (text : string) =
    text.EnumerateRunes() |> Seq.truncate length |> Seq.map _.ToString() |> String.concat ""

  let private caseName =
    function
    | AwaitsModeration -> "awaits-moderation"
    | Approved -> "approved"

  /// None when nobody needs a notification: the author rejected the comment.
  let ofComment (newCount : int) (posted : PostedComment) : NewCommentPayload option =
    let comment = posted.Comment
    let commenter =
      comment.Commenter.Name
      |> Option.map NonEmptyString.value
      |> Option.defaultValue (string comment.Commenter.EmailAddress)

    let payload case =
      Some
        { Case = case
          CommentId = string comment.Id
          Slug = Slug.value posted.Post
          PostTitle = posted.PostTitle |> NonEmptyString.value |> truncate maxTitleLength
          Commenter = commenter |> truncate maxCommenterLength
          Text = comment.Content |> NonEmptyString.value |> truncate maxTextLength
          NewCount = newCount }

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
         postTitle = payload.PostTitle
         commenter = payload.Commenter
         text = payload.Text
         newCount = payload.NewCount |},
      options
    )

// ====================================================================================================================
// Notify the author of a new comment
// ====================================================================================================================

[<RequireQualifiedAccess>]
module NotifyNewComment =

  type Dto = { CommentId : string }

  // Output

  type Error = unit

  type Notified =
    { Sent : RegisteredDevice list
      Removed : RegisteredDevice list
      /// A push that failed for another reason than `404` and `410`. The caller logs each one.
      Failed : (RegisteredDevice * DependencyError) list }

  type Event =
    | Notified of Notified
    /// The comment is gone or rejected, so nobody needs a notification.
    | Skipped of CommentId

  // Action

  type Action = Dto -> CancellableTask<ActionResult<Event, Error>>

  type Dependencies =
    abstract member GetPostedComment: CommentId -> DependencyResult<PostedComment option>
    /// The count of comments in the moderation queue
    abstract member CountNewComments: unit -> DependencyResult<int>
    abstract member GetDevicesOfAuthor: unit -> DependencyResult<RegisteredDevice list>
    abstract member SendPush: RegisteredDevice -> payload: byte array -> DependencyResult<PushOutcome>
    abstract member RecordPush: RegisteredDevice -> CommentId -> DependencyResult<unit>
    abstract member DeleteDevice: RegisteredDevice -> DependencyResult<unit>

  type private PushStep =
    | PushSent of RegisteredDevice
    | PushRemoved of RegisteredDevice
    | PushFailed of RegisteredDevice * DependencyError

  let withDependencies (dependencies : Dependencies) : Action =

    // A failure on one device never stops the push to the next device.
    let pushTo (payload : byte array) (commentId : CommentId) (device : RegisteredDevice) : CancellableTask<PushStep> =
      cancellableTask {
        let! sent = dependencies.SendPush device payload

        match sent with
        | Ok Delivered ->
          let! recorded = dependencies.RecordPush device commentId
          return recorded |> Result.map (fun () -> PushSent device) |> Result.defaultWith (fun error -> PushFailed(device, error))
        | Ok DeviceGone ->
          let! deleted = dependencies.DeleteDevice device
          return deleted |> Result.map (fun () -> PushRemoved device) |> Result.defaultWith (fun error -> PushFailed(device, error))
        | Error error -> return PushFailed(device, error)
      }

    let pushAll (payload : byte array) (commentId : CommentId) (devices : RegisteredDevice list) : CancellableTask<PushStep list> =
      cancellableTask {
        let results = ResizeArray<PushStep>()

        for device in devices do
          let! result = pushTo payload commentId device
          results.Add result

        return List.ofSeq results
      }

    fun dto -> cancellableTaskResult {
      let! commentId =
        CommentId.create dto.CommentId
        |> Result.mapError ActionError<Error>.Validation

      let! posted =
        dependencies.GetPostedComment commentId
        |> CancellableTaskResult.mapError ActionError<Error>.Dependency

      match posted with
      | None -> return Skipped commentId
      | Some posted ->
        let! newCount =
          dependencies.CountNewComments()
          |> CancellableTaskResult.mapError ActionError<Error>.Dependency

        match NewCommentPayload.ofComment newCount posted with
        | None -> return Skipped commentId
        | Some payload ->
          let! devices =
            dependencies.GetDevicesOfAuthor()
            |> CancellableTaskResult.mapError ActionError<Error>.Dependency

          let bytes = NewCommentPayload.toBytes payload
          let! results = pushAll bytes commentId devices |> CancellableTask.map Ok

          return
            Notified
              { Sent = results |> List.choose (function PushSent device -> Some device | _ -> None)
                Removed = results |> List.choose (function PushRemoved device -> Some device | _ -> None)
                Failed = results |> List.choose (function PushFailed(device, error) -> Some(device, error) | _ -> None) }
    }
