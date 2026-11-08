namespace TwoPoint.Http.Endpoints.Queues

open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Posts
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Communication.Email
open Azure.Data.Tables
open Azure.Messaging.ServiceBus
open Azure.Storage.Blobs
open Microsoft.Azure.Functions.Worker
open Microsoft.Extensions.Logging

open System
open System.Threading

type ProcessReplyEmailMessage(
  config: Config,
  emailClient: EmailClient,
  logger : ILogger<ProcessReplyEmailMessage>,
  postStoreClient: BlobServiceClient,
  tableServiceClient: TableServiceClient,
  texts : BlobTextCache
) =

  [<Function("Queues-ProcessReplyEmailMessage")>]
  member _.Run (
    [<ServiceBusTrigger(ReplyEmailQueue.Name, Connection = "TwoPointWebsiteServiceBus")>] message : ServiceBusReceivedMessage,
    ct : CancellationToken
  ) = task {
    let json = message.Body.FromJson<ReplyEmailMessage | null>()

    match json, config.ReplyEmail with
    | null, _ ->
      logger.LogError("Unable to process '{queue}' message. Failed to deserialize message from JSON", ReplyEmailQueue.Name)
    | _, None ->
      // Throw so that the message goes to the dead-letter queue, where it waits for the setting.
      logger.LogError("Unable to process '{queue}' message. The 'ReplyEmail' configuration section is missing", ReplyEmailQueue.Name)
      failwith "The 'ReplyEmail' configuration section is missing"
    | replyJson, Some replyEmailConfig ->
      logger.LogInformation(
        "Processing '{queue}' message of reply '{replyId}' on post '{post}'",
        ReplyEmailQueue.Name,
        replyJson.ReplyId,
        replyJson.Post
      )

      let postDependencies = PostDependencies(
        [],
        emailClient,
        config.Azure.EmailSender,
        tableServiceClient,
        logger
      )
      let postStore = PostStore(postStoreClient, logger, texts)

      let sendReplyEmail = SendReplyEmail.withDependencies { new SendReplyEmail.Dependencies with
        member _.GetPostedCommentById commentId = postDependencies.GetPostedCommentById commentId
        member _.GetReplyEmailRecord post reply = postDependencies.GetReplyEmailRecord post reply
        member _.GetPublishedBlob slug = postStore.GetPublishedBlob slug
        member _.SendReplyEmail recipient content = postDependencies.SendReplyEmail recipient content
        member _.RecordReplyEmailSent post reply sentDate = postDependencies.RecordReplyEmailSent post reply sentDate
        member _.GetUtcNow () = DateTime.UtcNow
        member _.Site = replyEmailConfig.SiteUri
        member _.Stage = config.Stage
      }

      let dto : SendReplyEmail.Dto =
        { Post = replyJson.Post |> Option.ofObj |> Option.defaultValue ""
          ReplyId = replyJson.ReplyId |> Option.ofObj |> Option.defaultValue "" }

      match! ct |> sendReplyEmail dto with
      | Ok (SendReplyEmail.Event.Sent (post, reply)) ->
        logger.LogInformation("Sent the reply email of reply '{replyId}' on post '{post}'", reply, post)
      | Ok (SendReplyEmail.Event.SentWithNoRecord (post, reply, error)) ->
        logger.LogError(
          "Sent the reply email of reply '{replyId}' on post '{post}', but the send record was not written: {error}",
          reply,
          post,
          error.DebugMessage
        )
      | Ok (SendReplyEmail.Event.Skipped (reply, reason)) ->
        logger.LogInformation("Skipped the reply email of reply '{replyId}': {reason}", reply, reason)
      | Error (ActionError.Validation errors) ->
        logger.LogError("Invalid '{queue}' message: {errors}", ReplyEmailQueue.Name, errors)
      | Error (ActionError.Logic ()) -> ()
      | Error (ActionError.Dependency error) ->
        // Throw so that Service Bus delivers the message again.
        logger.LogError("Unable to send a reply email: {error}", error.DebugMessage)
        failwith $"The reply email failed in a dependency: {error.Message}"
  }
