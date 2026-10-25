namespace TwoPoint.Http.Endpoints.Queues

open TwoPoint.Core.Notifications.Actions
open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Notifications
open TwoPoint.Dependencies.Posts
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Communication.Email
open Azure.Data.Tables
open Azure.Messaging.ServiceBus
open FsToolkit.ErrorHandling
open Microsoft.Azure.Functions.Worker
open Microsoft.Extensions.Logging

open System
open System.Threading

type ProcessNewCommentMessage(
  config: Config,
  emailClient: EmailClient,
  logger : ILogger<ProcessNewCommentMessage>,
  pushSetup : WebPushSetup,
  tableServiceClient: TableServiceClient
) =

  [<Function("Queues-ProcessNewCommentMessage")>]
  member _.Run (
    [<ServiceBusTrigger(NewCommentQueue.Name, Connection = "TwoPointWebsiteServiceBus")>] message : ServiceBusReceivedMessage,
    ct : CancellationToken
  ) = task {
    let json = message.Body.FromJson<NewCommentMessage | null>()

    match json with
    | null ->
      logger.LogError(
        "Unable to process '{queue}' message. Failed to deserialize message from JSON",
        NewCommentQueue.Name
      )
    | commentJson ->
      logger.LogInformation(
        "Processing '{queue}' message of comment '{commentId}'",
        NewCommentQueue.Name,
        commentJson.CommentId
      )

      let postDependencies = PostDependencies(
        [],
        emailClient,
        config.Azure.EmailSender,
        tableServiceClient,
        logger
      )
      let deviceDependencies = DeviceDependencies(tableServiceClient, logger)
      let pushDependencies = NewCommentPushDependencies(pushSetup, tableServiceClient, logger)

      let notifyNewComment = NotifyNewComment.withDependencies { new NotifyNewComment.Dependencies with
        member _.GetPostedComment commentId = postDependencies.GetPostedCommentById commentId
        member _.CountNewComments () = postDependencies.CountNewComments()
        member _.GetDevicesOfAuthor () = deviceDependencies.GetDevicesOfUser Access.authorUserId
        member _.SendPush device payload = pushDependencies.SendPush device payload
        member _.RecordPush device commentId = pushDependencies.RecordPush device commentId
        member _.DeleteDevice device = deviceDependencies.DeleteDevice device
      }

      match! ct |> notifyNewComment { NotifyNewComment.Dto.CommentId = commentJson.CommentId } with
      | Ok (NotifyNewComment.Event.Notified notified) ->
        logger.LogInformation(
          "Notified the author of comment '{commentId}': {sent} sent, {removed} removed",
          commentJson.CommentId,
          notified.Sent.Length,
          notified.Removed.Length
        )
        for device, error in notified.Failed do
          logger.LogError("Unable to push to device '{deviceId}': {error}", device.Id, error.DebugMessage)
      | Ok (NotifyNewComment.Event.Skipped commentId) ->
        logger.LogInformation("Skipped the notice of comment '{commentId}'", commentId)
      | Error (ActionError.Validation errors) ->
        logger.LogError("Invalid '{queue}' message: {errors}", NewCommentQueue.Name, errors)
      | Error (ActionError.Logic ()) -> ()
      | Error (ActionError.Dependency error) ->
        // Throw so that Service Bus delivers the message again.
        logger.LogError("Unable to notify the author of a new comment: {error}", error.DebugMessage)
        failwith $"The new-comment notice failed in a dependency: {error.Message}"
  }
