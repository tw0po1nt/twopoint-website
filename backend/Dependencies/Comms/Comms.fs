namespace TwoPoint.Dependencies.Comms

open TwoPoint.Dependencies.Util
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

/// `Content` is the HTML part. A message with no `PlainText` has only the HTML part.
type EmailMessage =
  { Subject : NonEmptyString
    Content : NonEmptyString
    PlainText : NonEmptyString option
    Recipient : EmailAddress }
  
module EmailMessage =
  
  let create subject content recipient = validation {
    let! subject = subject |> NonEmptyString.create (Some (nameof subject))
    let! content = content |> NonEmptyString.create (Some (nameof content))
    let! recipient = recipient |> EmailAddress.create
    return { Subject = subject; Content = content; PlainText = None; Recipient = recipient }
  }

  let createWithPlainText subject content plainText (recipient : EmailAddress) = validation {
    let! subject = subject |> NonEmptyString.create (Some (nameof subject))
    and! content = content |> NonEmptyString.create (Some (nameof content))
    and! plainText = plainText |> NonEmptyString.create (Some (nameof plainText))
    return { Subject = subject; Content = content; PlainText = Some plainText; Recipient = recipient }
  }
  
type ICommsService =
  abstract member SendEmail: message: EmailMessage -> CancellableTaskResult<OperationId, DependencyError>

[<RequireQualifiedAccess>]
module internal CommsService =
  
  open Azure
  open Azure.Communication.Email
  open Microsoft.Extensions.Logging

  open System

  let private runComms logger op fn =
    runDependencyWithLogging Dependency.Communications logger op fn

  /// ACS has the email at this step, so a failed tracking row must not fail the send and cause a second email.
  let track
    (logger : ILogger)
    (createEmail : DbEmail -> CancellableTaskResult<DbEmail, DependencyError>)
    (email : DbEmail)
    : CancellableTask<OperationId> =
    cancellableTask {
      match! createEmail email with
      | Ok (dbEmail : DbEmail) -> return dbEmail.OperationId
      | Error error ->
        logger.LogWarning("ACS took the email {operationId}, but its tracking row failed: {error}", email.OperationId, error)
        return email.OperationId
    }

  let withDb
    (emailClient : EmailClient)
    (commsDb : ICommsDb)
    (system : ISystemDependencies)
    (logger : ILogger)
    (sender : TwoPoint.Core.Shared.EmailAddress)
    =
    { new ICommsService with
      
      member _.SendEmail message =
        runComms logger $"{nameof ICommsService}.SendEmail"
        <| cancellableTaskResult {
          let! ct = CancellableTask.getCancellationToken()
          // Send the message
          let recipient = message.Recipient.ToString()
          let! emailSendOperation = emailClient.SendAsync(
            wait = WaitUntil.Started,
            senderAddress = sender.ToString(),
            recipientAddress = recipient,
            subject = message.Subject.ToString(),
            htmlContent = message.Content.ToString(),
            plainTextContent = (message.PlainText |> Option.map NonEmptyString.value |> Option.toObj),
            cancellationToken = ct
          )
          
          let operationId, operationIdSource =
            match emailSendOperation.Id with
            | "NOT_SET" -> system.NewGuid().ToString(), "twopoint"
            | otherwise -> otherwise, "azure"

          // Track the operation
          let email =
            { DbEmail.OperationId = operationId
              OperationIdSource = operationIdSource
              CreatedDate = system.GetUtcNow()
              Recipient = recipient
              Status = "sent"
              StatusReason = None }

          return! track logger commsDb.CreateEmail email |> CancellableTask.map Ok
        }
    }

  let live (emailClient : EmailClient) tableServiceClient (system : ISystemDependencies) logger sender =
    withDb emailClient (CommsDb.live tableServiceClient logger) system logger sender
